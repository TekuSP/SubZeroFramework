using System.Reactive.Linq;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public sealed class GrpcHardwareInfoClient : IHardwareInfoClient, IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly HardwareInfoService.HardwareInfoServiceClient _client;
    private readonly IObservable<HardwareInfoSnapshot> _sharedHardwareInfoStream;

    public GrpcHardwareInfoClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new HardwareInfoService.HardwareInfoServiceClient(_channelFactory.Channel);
        _sharedHardwareInfoStream = _channelFactory.ShareLatest(CreateHardwareInfoStream());
    }

    public async Task<HardwareInfoSnapshot> GetHardwareInfoAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.GetHardwareInfoAsync(new GetHardwareInfoRequest(), cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapHardwareInfoReply(reply);
    }

    public IObservable<HardwareInfoSnapshot> WatchHardwareInfo() => _sharedHardwareInfoStream;

    public void Dispose()
    {
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
                        await Task.Delay(ReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
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

    private static HardwareInfoSnapshot CreateUnavailableSnapshot(string message)
    {
        return new HardwareInfoSnapshot
        {
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = false,
            LastError = message,
        };
    }

    private static HardwareInfoSnapshot MapHardwareInfoReply(HardwareInfoReply reply)
    {
        return new HardwareInfoSnapshot
        {
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
            LastError = string.IsNullOrEmpty(reply.LastError) ? null : reply.LastError,
            OperatingSystem = MapOperatingSystem(reply.OperatingSystem),
            ComputerSystem = MapComputerSystem(reply.ComputerSystem),
            Motherboard = MapMotherboard(reply.Motherboard),
            Bios = MapBios(reply.Bios),
            MemoryStatus = MapMemoryStatus(reply.MemoryStatus),
            Cpus = reply.Cpus.Select(MapCpu).ToImmutableArray(),
            MemoryModules = reply.MemoryModules.Select(MapMemoryModule).ToImmutableArray(),
            VideoControllers = reply.VideoControllers.Select(MapVideoController).ToImmutableArray(),
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
            reply.VideoProcessor);
    }
}
