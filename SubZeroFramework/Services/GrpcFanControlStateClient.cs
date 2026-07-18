using System.Reactive.Linq;

using DynamicData;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFanControlStateClient : IFanControlStateClient, IDisposable
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkTelemetryService.FrameworkTelemetryServiceClient _client;
    private readonly IObservable<IChangeSet<FanControlStateSnapshot, int>> _sharedControlStates;
    private bool _disposed;

    public GrpcFanControlStateClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkTelemetryService.FrameworkTelemetryServiceClient(_channelFactory.Channel);
        _sharedControlStates = _channelFactory.ShareLatest(CreateControlStatesStream());
    }

    public IObservable<IChangeSet<FanControlStateSnapshot, int>> WatchFanControlStates()
    {
        ThrowIfDisposed();
        return _sharedControlStates;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private IObservable<IChangeSet<FanControlStateSnapshot, int>> CreateControlStatesStream()
    {
        return Observable.Create<IChangeSet<FanControlStateSnapshot, int>>(observer =>
        {
            var controlStates = new SourceCache<FanControlStateSnapshot, int>(state => state.FanIndex);
            var cancellationSource = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cancellationSource.IsCancellationRequested)
                {
                    AsyncServerStreamingCall<FanControlStateChangeBatchReply>? call = null;

                    try
                    {
                        call = _client.WatchFanControlStates(new WatchFanControlStatesRequest(), cancellationToken: cancellationSource.Token);

                        using var connection = controlStates.Connect().Subscribe(observer);

                        while (await call.ResponseStream.MoveNext(cancellationSource.Token).ConfigureAwait(false))
                        {
                            var changes = call.ResponseStream.Current.Changes;
                            if (changes.Count == 0)
                            {
                                continue;
                            }

                            controlStates.Edit(updater =>
                            {
                                foreach (var change in changes)
                                {
                                    ApplyControlStateChange(updater, change);
                                }
                            });
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

                controlStates.Dispose();
                observer.OnCompleted();
            }, cancellationSource.Token);

            return () =>
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            };
        });
    }

    private static void ApplyControlStateChange(ISourceUpdater<FanControlStateSnapshot, int> controlStates, FanControlStateChangeReply reply)
    {
        if (reply.ChangeKind == TelemetryChangeKind.Remove)
        {
            var existingState = controlStates.Lookup(reply.FanIndex);
            if (existingState.HasValue)
            {
                controlStates.AddOrUpdate(existingState.Value with { IsAvailable = false });
            }

            return;
        }

        controlStates.AddOrUpdate(new FanControlStateSnapshot
        {
            FanIndex = reply.FanIndex,
            DisplayName = reply.DisplayName,
            Mode = ParseFanControlMode(reply.ControlMode),
            CustomCurvePoints = reply.CustomCurvePoints.Count == 0
                ? ImmutableSortedDictionary<int, double>.Empty
                : reply.CustomCurvePoints.ToImmutableSortedDictionary(point => point.TemperatureCelsius, point => point.FanDutyPercent),
            DrivingTemperatureAggregation = ParseTemperatureAggregationMode(reply.DrivingTemperatureAggregation),
            DrivingSensorIndices = [.. reply.DrivingSensorIndices],
            HasActiveOverride = reply.HasActiveOverride,
            LastAutoRestoreAttemptFailed = reply.LastAutoRestoreAttemptFailed,
            LastAutoRestoreAttemptAt = reply.HasLastAutoRestoreAttempt
                ? DateTimeOffset.FromUnixTimeMilliseconds(reply.LastAutoRestoreAttemptAtUnixTimeMilliseconds)
                : null,
            LastAutoRestoreError = string.IsNullOrWhiteSpace(reply.LastAutoRestoreError)
                ? null
                : reply.LastAutoRestoreError,
            LastDutyPercent = reply.HasLastDutyPercent
                ? reply.LastDutyPercent
                : null,
            ActiveCurveSlot = reply.ActiveCurveSlot,
            CurveProfiles = [.. reply.CurveProfiles.Select(ParseCurveProfile)],
            LinkedLeaderIndex = reply.HasLinkedLeaderIndex ? reply.LinkedLeaderIndex : null,
            CpuUsageModifierStrength = reply.HasCpuUsageModifierStrength && double.IsFinite(reply.CpuUsageModifierStrength)
                ? reply.CpuUsageModifierStrength
                : null,
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
        });
    }

    private static FanCurveProfileSnapshot ParseCurveProfile(FanCurveProfileReply reply)
    {
        return new FanCurveProfileSnapshot
        {
            Slot = reply.Slot,
            Name = string.IsNullOrWhiteSpace(reply.Name) ? null : reply.Name,
            IsConfigured = reply.IsConfigured,
            CurvePoints = reply.Points.Count == 0
                ? ImmutableSortedDictionary<int, double>.Empty
                : reply.Points.ToImmutableSortedDictionary(point => point.TemperatureCelsius, point => point.FanDutyPercent),
            DrivingTemperatureAggregation = ParseTemperatureAggregationMode(reply.Aggregation),
            DrivingSensorIndices = [.. reply.DrivingSensorIndices],
            FollowFanIndex = reply.HasFollowTarget ? reply.FollowFanIndex : null,
        };
    }

    private static FanControlMode ParseFanControlMode(FanControlModeValue value)
    {
        return value switch
        {
            FanControlModeValue.Auto => FanControlMode.Auto,
            FanControlModeValue.Manual => FanControlMode.Manual,
            FanControlModeValue.CustomCurve => FanControlMode.CustomCurve,
            FanControlModeValue.Max => FanControlMode.Max,
            _ => FanControlMode.Auto,
        };
    }

    private static TemperatureAggregationMode ParseTemperatureAggregationMode(TemperatureAggregationModeValue value)
    {
        return value switch
        {
            TemperatureAggregationModeValue.Average => TemperatureAggregationMode.Average,
            TemperatureAggregationModeValue.Median => TemperatureAggregationMode.Median,
            TemperatureAggregationModeValue.Maximum => TemperatureAggregationMode.Maximum,
            TemperatureAggregationModeValue.Minimum => TemperatureAggregationMode.Minimum,
            _ => TemperatureAggregationMode.Maximum,
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
