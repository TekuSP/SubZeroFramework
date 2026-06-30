using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

/// <summary>
/// gRPC consumer of <c>WatchPowerDelivery</c>. Mirrors <see cref="GrpcFrameworkStatusClient"/>: a single
/// reconnecting server-streaming call projected into a shared, replay-latest observable of PD port state.
/// </summary>
public sealed class GrpcPowerDeliveryClient : IPowerDeliveryClient
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _client;
    private readonly IObservable<IReadOnlyList<PowerDeliveryPortStatus>> _sharedStream;

    public GrpcPowerDeliveryClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(_channelFactory.Channel);
        _sharedStream = _channelFactory.ShareLatest(CreatePortsStream());
    }

    public IObservable<IReadOnlyList<PowerDeliveryPortStatus>> WatchPorts() => _sharedStream;

    private IObservable<IReadOnlyList<PowerDeliveryPortStatus>> CreatePortsStream()
    {
        return System.Reactive.Linq.Observable.Create<IReadOnlyList<PowerDeliveryPortStatus>>(observer =>
        {
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<PowerDeliveryReply>? call = null;

                    try
                    {
                        call = _client.WatchPowerDelivery(new WatchPowerDeliveryRequest(), cancellationToken: cancellationSource.Token);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            observer.OnNext(MapPorts(call.ResponseStream.Current));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (RpcException) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext([]);
                    }
                    catch (Exception) when (!cancellationSource.IsCancellationRequested)
                    {
                        observer.OnNext([]);
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

    private static IReadOnlyList<PowerDeliveryPortStatus> MapPorts(PowerDeliveryReply reply)
    {
        var ports = new List<PowerDeliveryPortStatus>(reply.Ports.Count);
        foreach (var port in reply.Ports)
        {
            ports.Add(new PowerDeliveryPortStatus
            {
                SlotIndex = port.SlotIndex,
                IsPresent = port.IsPresent,
                IsActivePort = port.IsActivePort,
                HasContract = port.HasPowerDeliveryContract,
                CState = port.CState,
                PowerRole = port.PowerRole,
                DataRole = port.DataRole,
                CcPolarity = port.CcPolarity,
                VoltageVolts = port.VoltageVolts,
                CurrentAmperes = port.CurrentAmperes,
                IsVconnActive = port.IsVconnActive,
                IsEprActive = port.IsEprActive,
                IsEprSupported = port.IsEprSupported,
                AltModeFlags = (byte)port.AltModeFlags,
                CardType = port.CardType,
                DataLane = port.DataLane,
                DisplayPortCapability = port.DisplayPortCapability,
                SupportsCharging = port.CapabilitySupportsCharging,
                MaxChargeWatts = port.MaxChargeWatts,
                UsbAHighPower = port.UsbAHighPower,
                CapabilityDocumented = port.CapabilityDocumented,
                PortSource = port.PortSource,
            });
        }

        return ports;
    }
}
