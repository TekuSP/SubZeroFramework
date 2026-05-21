using DynamicData;

using System.Reactive.Linq;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public sealed class GrpcHardwareInfoClient : IHardwareInfoClient, IDisposable
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly HardwareInfoService.HardwareInfoServiceClient _client;
    private readonly IObservable<HardwareInfoSnapshot> _sharedHardwareInfoStream;
    private readonly RefCountedObservableCache<TimeSpan, IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>> _historyStreams = new();
    private bool _disposed;

    public GrpcHardwareInfoClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new HardwareInfoService.HardwareInfoServiceClient(_channelFactory.Channel);
        _sharedHardwareInfoStream = _channelFactory.ShareLatest(CreateHardwareInfoStream());
    }

    public async Task<HardwareInfoSnapshot> GetHardwareInfoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.GetHardwareInfoAsync(new GetHardwareInfoRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapHardwareInfoReply(reply);
    }

    public IObservable<HardwareInfoSnapshot> WatchHardwareInfo()
    {
        ThrowIfDisposed();
        return _sharedHardwareInfoStream;
    }

    public IObservable<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>> WatchHardwareInfoHistory(TimeSpan historyWindow)
    {
        ThrowIfDisposed();

        if (historyWindow <= TimeSpan.Zero || historyWindow > TelemetryHistoryLimits.MaximumHistoryWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(historyWindow), $"History window must be between {TimeSpan.Zero} and {TelemetryHistoryLimits.MaximumHistoryWindow}.");
        }

        return _historyStreams.GetOrAdd(historyWindow, () => CreateHardwareInfoHistoryStream(historyWindow));
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private IObservable<HardwareInfoSnapshot> CreateHardwareInfoStream()
    {
        return Observable.Create<HardwareInfoSnapshot>(observer =>
        {
            var cancellationSource = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<HardwareInfoReply>? call = null;
                    try
                    {
                        call = _client.WatchHardwareInfo(new WatchHardwareInfoRequest(), cancellationToken: cancellationSource.Token);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            var snapshot = MapHardwareInfoReply(call.ResponseStream.Current);
                            observer.OnNext(snapshot);
                        }

                        if (!cancellationSource.IsCancellationRequested)
                        {
                            observer.OnNext(CreateUnavailableSnapshot("The hardware info service stream ended unexpectedly."));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext(CreateUnavailableSnapshot($"Unable to connect to HardwareInfo service. {exception.Status.Detail}"));
                    }
                    catch (Exception exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext(CreateUnavailableSnapshot($"Unable to connect to HardwareInfo service. {exception.Message}"));
                    }
                    finally
                    {
                        call?.Dispose();
                    }

                    try
                    {
                        await Task.Delay(GrpcTransportDefaults.StreamReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private IObservable<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>> CreateHardwareInfoHistoryStream(TimeSpan historyWindow)
    {
        return Observable.Create<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>>(observer =>
        {
            var history = new SourceCache<HistoricalRecord<HardwareInfoSnapshot>, long>(record => record.SampleId);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<HardwareInfoHistoryChangeBatchReply>? call = null;

                    try
                    {
                        call = _client.WatchHardwareInfoHistory(new WatchHardwareInfoHistoryRequest
                        {
                            HistoryWindowSeconds = checked((int)Math.Ceiling(historyWindow.TotalSeconds)),
                        }, cancellationToken: cancellationSource.Token);

                        using var connection = history.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            foreach (var change in call.ResponseStream.Current.Changes)
                            {
                                ApplyHardwareInfoHistoryChange(history, change);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    catch (Exception) when (!cancellationSource.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        call?.Dispose();
                    }

                    try
                    {
                        await Task.Delay(GrpcTransportDefaults.StreamReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                }

                history.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private static HardwareInfoSnapshot CreateUnavailableSnapshot(string message)
    {
        return new HardwareInfoSnapshot
        {
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = false,
            LastError = message,
        };
    }

    private static void ApplyHardwareInfoHistoryChange(SourceCache<HistoricalRecord<HardwareInfoSnapshot>, long> history, HardwareInfoHistoryChangeReply reply)
    {
        if (reply.ChangeKind == TelemetryChangeKind.Remove)
        {
            history.RemoveKey(reply.SampleId);
            return;
        }

        history.AddOrUpdate(new HistoricalRecord<HardwareInfoSnapshot>(
            reply.SampleId,
            DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            MapHardwareInfoReply(reply.Snapshot)));
    }

    private static HardwareInfoSnapshot MapHardwareInfoReply(HardwareInfoReply reply)
    {
        return new HardwareInfoSnapshot
        {
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
            LastError = string.IsNullOrEmpty(reply.LastError) ? null : reply.LastError,
            Inventory = new HardwareInfoInventorySnapshot
            {
                OperatingSystem = MapOperatingSystem(reply.OperatingSystem),
                ComputerSystem = MapComputerSystem(reply.ComputerSystem),
                Motherboard = MapMotherboard(reply.Motherboard),
                Bios = MapBios(reply.Bios),
                MemoryModules = reply.MemoryModules.Select(MapMemoryModule).ToImmutableArray(),
                Drives = reply.Drives.Select(MapDrive).ToImmutableArray(),
                NetworkAdapters = reply.NetworkAdapters.Select(MapNetworkAdapter).ToImmutableArray(),
            },
            Runtime = new HardwareInfoRuntimeSnapshot
            {
                MemoryStatus = MapMemoryStatus(reply.MemoryStatus),
                Cpus = reply.Cpus.Select(MapCpu).ToImmutableArray(),
                Monitors = reply.Monitors.Select(MapMonitor).ToImmutableArray(),
                VideoControllers = reply.VideoControllers.Select(MapVideoController).ToImmutableArray(),
            },

        };
    }

    private static HardwareInfoOperatingSystem? MapOperatingSystem(HardwareInfoOperatingSystemReply reply)
    {
        return reply is null ? null : new HardwareInfoOperatingSystem(reply.Name, reply.VersionString);
    }

    private static HardwareInfoComputerSystem? MapComputerSystem(HardwareInfoComputerSystemReply reply)
    {
        return reply is null
            ? null
            : new HardwareInfoComputerSystem(
                reply.Vendor,
                reply.Caption,
                reply.Description,
                reply.Name,
                reply.SkuNumber,
                reply.Uuid,
                reply.Version);
    }

    private static HardwareInfoMotherboard? MapMotherboard(HardwareInfoMotherboardReply reply)
    {
        return reply is null ? null : new HardwareInfoMotherboard(reply.Manufacturer, reply.Product, reply.SerialNumber);
    }

    private static HardwareInfoBios? MapBios(HardwareInfoBiosReply reply)
    {
        return reply is null
            ? null
            : new HardwareInfoBios(
                reply.Manufacturer,
                reply.Caption,
                reply.Description,
                reply.Name,
                reply.Version,
                reply.ReleaseDate,
                reply.SerialNumber,
                reply.SoftwareElementId);
    }

    private static HardwareInfoMemoryStatus? MapMemoryStatus(HardwareInfoMemoryStatusReply reply)
    {
        return reply is null
            ? null
            : new HardwareInfoMemoryStatus(
                reply.TotalPhysical,
                reply.AvailablePhysical,
                reply.TotalPageFile,
                reply.AvailablePageFile,
                reply.TotalVirtual,
                reply.AvailableVirtual,
                reply.AvailableExtendedVirtual);
    }

    private static HardwareInfoCpu MapCpu(HardwareInfoCpuReply reply)
    {
        return new HardwareInfoCpu(
            reply.Name,
            reply.Caption,
            reply.Description,
            reply.Manufacturer,
            checked((int)reply.Cores),
            checked((int)reply.LogicalProcessors),
            checked((int)reply.CurrentClockSpeedMhz),
            checked((int)reply.MaxClockSpeedMhz),
            reply.ProcessorId,
            reply.SocketDesignation,
            checked((int)reply.L1CacheSizeKb),
            checked((int)reply.L2CacheSizeKb),
            checked((int)reply.L3CacheSizeKb),
            reply.SecondLevelAddressTranslationExtensions,
            reply.VirtualizationFirmwareEnabled,
            reply.VmMonitorModeExtensions,
            reply.HasPercentProcessorTime
                ? reply.PercentProcessorTime
                : null,
            reply.CpuCores.Select(MapCpuCore).ToImmutableArray());
    }

    private static HardwareInfoCpuCore MapCpuCore(HardwareInfoCpuCoreReply reply)
    {
        return new HardwareInfoCpuCore(
            reply.Name,
            reply.PercentProcessorTime);
    }

    private static HardwareInfoMemoryModule MapMemoryModule(HardwareInfoMemoryModuleReply reply)
    {
        return new HardwareInfoMemoryModule(
            reply.BankLabel,
            reply.CapacityBytes,
            reply.DataWidth,
            reply.MemoryType,
            reply.FormFactor,
            reply.SpeedMhz,
            reply.MaxVoltage,
            reply.MinVoltage,
            reply.Manufacturer,
            reply.PartNumber,
            reply.SerialNumber);
    }

    private static HardwareInfoDrive MapDrive(HardwareInfoDriveReply reply)
    {
        return new HardwareInfoDrive(
            reply.Index,
            reply.Name,
            reply.Model,
            reply.Caption,
            reply.Description,
            reply.Manufacturer,
            reply.MediaType,
            reply.SerialNumber,
            reply.FirmwareRevision,
                reply.Size,
                reply.FreeSpace);
    }

    private static HardwareInfoNetworkAdapter MapNetworkAdapter(HardwareInfoNetworkAdapterReply reply)
    {
        return new HardwareInfoNetworkAdapter(
            reply.Name,
            reply.NetConnectionId,
            reply.ProductName,
            reply.Caption,
            reply.Description,
            reply.Manufacturer,
            reply.AdapterType,
            reply.MacAddress,
            reply.Speed,
            reply.IpAddresses.ToImmutableArray(),
            reply.DefaultGateways.ToImmutableArray());
    }

    private static HardwareInfoVideoController MapVideoController(HardwareInfoVideoControllerReply reply)
    {
        return new HardwareInfoVideoController(
            reply.AdapterRam,
            reply.Caption,
            reply.CurrentBitsPerPixel,
            reply.CurrentHorizontalResolution,
            reply.CurrentNumberOfColors,
            reply.CurrentRefreshRate,
            reply.CurrentVerticalResolution,
            reply.Description,
            reply.DriverDate,
            reply.DriverVersion,
            reply.Manufacturer,
            reply.MaxRefreshRate,
            reply.MinRefreshRate,
            reply.Name,
            reply.VideoModeDescription,
            reply.VideoProcessor,
            reply.LinkedMonitorDisplayNames.ToImmutableArray());
    }

    private static HardwareInfoMonitor MapMonitor(HardwareInfoMonitorReply reply)
    {
        return new HardwareInfoMonitor(
            reply.Active,
            reply.Caption,
            reply.Description,
            reply.ManufacturerName,
            reply.MonitorManufacturer,
            reply.MonitorType,
            reply.Name,
            reply.PixelsPerXLogicalInch,
            reply.PixelsPerYLogicalInch,
            reply.ProductCodeId,
            reply.SerialNumberId,
            reply.UserFriendlyName,
            checked((ushort)reply.WeekOfManufacture),
                checked((ushort)reply.YearOfManufacture),
                reply.CurrentHorizontalResolution,
                reply.CurrentVerticalResolution,
                reply.CurrentRefreshRate,
                reply.LinkedVideoControllerDisplayNames.ToImmutableArray());
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
