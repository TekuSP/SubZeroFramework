using DynamicData;
using FrameworkDotnet.Enums;
using Grpc.Core;

using SubZeroFramework.GrpcContracts;

using System.Reactive.Linq;

namespace SubZeroFramework.Services;

public sealed class GrpcFanCapabilityClient : IFanCapabilityClient, IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

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
                        await Task.Delay(ReconnectDelay, cancellationSource.Token).ConfigureAwait(false);
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
            fanCapabilities.Remove(reply.FanIndex);
            return;
        }

        fanCapabilities.AddOrUpdate(new FanCapabilityState
        {
            FanIndex = reply.FanIndex,
            DisplayName = reply.DisplayName,
            Features = (FrameworkFanFeaturesState)reply.Features,
            SupportsFanControl = reply.SupportsFanControl,
            SupportsThermalReporting = reply.SupportsThermalReporting,
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
        });
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}