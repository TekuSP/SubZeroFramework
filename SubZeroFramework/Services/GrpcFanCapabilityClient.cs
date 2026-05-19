using DynamicData;
using FrameworkDotnet.Enums;
using Grpc.Core;

using SubZeroFramework.GrpcContracts;

using System.Reactive.Linq;

namespace SubZeroFramework.Services;

public sealed class GrpcFanCapabilityClient : IFanCapabilityClient, IDisposable
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _client;
    private readonly IObservable<IChangeSet<FanCapabilityState, int>> _sharedFanCapabilities;
    private bool _disposed;

    public GrpcFanCapabilityClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(_channelFactory.Channel);
        _sharedFanCapabilities = _channelFactory.ShareLatest(CreateFanCapabilitiesStream());
    }

    /// <summary>
    /// Watches the current fan capability set.
    /// </summary>
    public IObservable<IChangeSet<FanCapabilityState, int>> WatchFanCapabilities()
    {
        ThrowIfDisposed();
        return _sharedFanCapabilities;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private IObservable<IChangeSet<FanCapabilityState, int>> CreateFanCapabilitiesStream()
    {
        return Observable.Create<IChangeSet<FanCapabilityState, int>>(observer =>
        {
            var fanCapabilities = new SourceCache<FanCapabilityState, int>(capability => capability.FanIndex);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<FanCapabilityChangeBatchReply>? call = null;

                    try
                    {
                        call = _client.WatchFanCapabilities(new WatchFanCapabilitiesRequest(), cancellationToken: cancellationSource.Token);

                        using var connection = fanCapabilities.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            foreach (var change in call.ResponseStream.Current.Changes)
                            {
                                ApplyFanCapabilityChange(fanCapabilities, change);
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

                fanCapabilities.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private static void ApplyFanCapabilityChange(SourceCache<FanCapabilityState, int> fanCapabilities, FanCapabilityChangeReply reply)
    {
        if (reply.ChangeKind == TelemetryChangeKind.Remove)
        {
            var existingCapability = fanCapabilities.Lookup(reply.FanIndex);
            if (existingCapability.HasValue)
            {
                fanCapabilities.AddOrUpdate(existingCapability.Value with { IsAvailable = false });
            }

            return;
        }

        fanCapabilities.AddOrUpdate(new FanCapabilityState
        {
            FanIndex = reply.FanIndex,
            DisplayName = reply.DisplayName,
            Features = (FrameworkFanFeaturesState)reply.Features,
            SupportsFanControl = reply.SupportsFanControl,
            SupportsThermalReporting = reply.SupportsThermalReporting,
            MaximumSpeedRpm = reply.MaximumSpeedRpm,
            CoolingDetails = MapCoolingDetails(reply.CoolingDetails),
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
        });
    }

    private static FrameworkCoolingDetails? MapCoolingDetails(FrameworkCoolingDetailsReply? reply)
    {
        if (reply is null)
        {
            return null;
        }

        return reply.DetailsCase switch
        {
            FrameworkCoolingDetailsReply.DetailsOneofCase.FrameworkLaptop12 => new FrameworkLaptop12CoolingDetails
            {
                ProcessorSupport = reply.FrameworkLaptop12.ProcessorSupport,
                ThermalCapacity = reply.FrameworkLaptop12.ThermalCapacity,
                HeatPipeConfiguration = reply.FrameworkLaptop12.HeatPipeConfiguration,
                FanDimensions = MapCoolingFanDimensions(reply.FrameworkLaptop12.FanDimensions),
                ThermalInterfaceMaterial = reply.FrameworkLaptop12.ThermalInterfaceMaterial,
                FirmwareOperatingRangeRpm = MapFanSpeedRange(reply.FrameworkLaptop12.FirmwareOperatingRangeRpm),
                MaximumPhysicalLimitRpm = reply.FrameworkLaptop12.MaximumPhysicalLimitRpm,
            },
            FrameworkCoolingDetailsReply.DetailsOneofCase.FrameworkLaptop13 => new FrameworkLaptop13CoolingDetails
            {
                ProcessorSupport = reply.FrameworkLaptop13.ProcessorSupport,
                ChassisMaterial = reply.FrameworkLaptop13.ChassisMaterial,
                ApproximateFirmwareIdleSpeedRpm = reply.FrameworkLaptop13.ApproximateFirmwareIdleSpeedRpm,
                ApproximateUserTunedIdleSpeedRpm = reply.FrameworkLaptop13.ApproximateUserTunedIdleSpeedRpm,
                MaximumFirmwareLimitRpm = reply.FrameworkLaptop13.MaximumFirmwareLimitRpm,
                ApproximatePhysicalMaximumRpm = reply.FrameworkLaptop13.ApproximatePhysicalMaximumRpm,
            },
            FrameworkCoolingDetailsReply.DetailsOneofCase.FrameworkLaptop16 => new FrameworkLaptop16CoolingDetails
            {
                ProcessorSupport = reply.FrameworkLaptop16.ProcessorSupport,
                PrimaryCpuThermalInterfaceMaterial = reply.FrameworkLaptop16.PrimaryCpuThermalInterfaceMaterial,
                ShellFanDimensions = MapCoolingFanDimensions(reply.FrameworkLaptop16.ShellFanDimensions),
                GraphicsFanDimensions = MapCoolingFanDimensions(reply.FrameworkLaptop16.GraphicsFanDimensions),
                ExpansionBayPowerLimitWatts = reply.FrameworkLaptop16.ExpansionBayPowerLimitWatts,
                StandardFirmwareMaximumRpm = reply.FrameworkLaptop16.StandardFirmwareMaximumRpm,
                ApproximateThermalStressMaximumRpm = reply.FrameworkLaptop16.ApproximateThermalStressMaximumRpm,
            },
            FrameworkCoolingDetailsReply.DetailsOneofCase.FrameworkDesktop => new FrameworkDesktopCoolingDetails
            {
                Platform = reply.FrameworkDesktop.Platform,
                SupportedFanOptions = [.. reply.FrameworkDesktop.SupportedFanOptions.Select(MapDesktopFanOption)],
            },
            FrameworkCoolingDetailsReply.DetailsOneofCase.None => null,
            _ => null,
        };
    }

    private static CoolingFanDimensions MapCoolingFanDimensions(CoolingFanDimensionsReply reply)
    {
        return new CoolingFanDimensions
        {
            WidthMillimeters = reply.WidthMillimeters,
            HeightMillimeters = reply.HeightMillimeters,
            ThicknessMillimeters = reply.ThicknessMillimeters,
            IsCircular = reply.IsCircular,
        };
    }

    private static FanSpeedRange MapFanSpeedRange(FanSpeedRangeReply reply)
    {
        return new FanSpeedRange
        {
            MinimumRpm = reply.MinimumRpm,
            MaximumRpm = reply.MaximumRpm,
        };
    }

    private static FrameworkDesktopFanOption MapDesktopFanOption(FrameworkDesktopFanOptionReply reply)
    {
        return new FrameworkDesktopFanOption
        {
            ModelName = reply.ModelName,
            FanDimensions = MapCoolingFanDimensions(reply.FanDimensions),
            ConnectorType = reply.ConnectorType,
            MaximumAirflowCfm = reply.MaximumAirflowCfm,
            AlternateAirflowDisplay = string.IsNullOrWhiteSpace(reply.AlternateAirflowDisplay) ? null : reply.AlternateAirflowDisplay,
            AcousticNoiseDisplay = reply.AcousticNoiseDisplay,
            MaximumFanSpeedRpm = reply.MaximumFanSpeedRpm,
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}