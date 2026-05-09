using DynamicData;

using FrameworkDotnet.Enums;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

using System.Reactive.Linq;

namespace SubZeroFramework.Services;

public sealed class GrpcFanStateClient : IFanStateClient, IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _client;
    private readonly IObservable<IChangeSet<FanStateSnapshot, int>> _sharedFanStates;
    private bool _disposed;

    public GrpcFanStateClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(_channelFactory.Channel);
        _sharedFanStates = _channelFactory.ShareLatest(CreateFanStatesStream());
    }

    public IObservable<IChangeSet<FanStateSnapshot, int>> WatchFanStates()
    {
        ThrowIfDisposed();
        return _sharedFanStates;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private IObservable<IChangeSet<FanStateSnapshot, int>> CreateFanStatesStream()
    {
        return Observable.Create<IChangeSet<FanStateSnapshot, int>>(observer =>
        {
            var fanStates = new SourceCache<FanStateSnapshot, int>(fanState => fanState.FanIndex);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<FanStateChangeBatchReply>? call = null;

                    try
                    {
                        call = _client.WatchFanStates(new WatchFanStatesRequest(), cancellationToken: cancellationSource.Token);

                        using var connection = fanStates.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            foreach (var change in call.ResponseStream.Current.Changes)
                            {
                                ApplyFanStateChange(fanStates, change);
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

                fanStates.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private static void ApplyFanStateChange(SourceCache<FanStateSnapshot, int> fanStates, FanStateChangeReply reply)
    {
        if (reply.ChangeKind == TelemetryChangeKind.Remove)
        {
            var existingState = fanStates.Lookup(reply.FanIndex);
            if (existingState.HasValue)
            {
                fanStates.AddOrUpdate(existingState.Value with { IsAvailable = false });
            }

            return;
        }

        fanStates.AddOrUpdate(new FanStateSnapshot
        {
            FanIndex = reply.FanIndex,
            DisplayName = reply.DisplayName,
            FanState = ParseFanState(reply.FanState),
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
        });
    }

    private static FrameworkFanState ParseFanState(FanStateValue fanState)
    {
        return fanState switch
        {
            FanStateValue.Ok => FrameworkFanState.Ok,
            FanStateValue.NotPresent => FrameworkFanState.NotPresent,
            FanStateValue.Stalled => FrameworkFanState.Stalled,
            _ => FrameworkFanState.NotPresent,
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}