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
            CoolingRole = reply.CoolingRole switch
            {
                FanCoolingRoleValue.Cpu => FanCoolingRole.Cpu,
                FanCoolingRoleValue.Gpu => FanCoolingRole.Gpu,
                FanCoolingRoleValue.System => FanCoolingRole.System,
                _ => FanCoolingRole.Unknown,
            },
            Mode = ParseFanControlMode(reply.ControlMode),
            CustomCurvePoints = reply.CustomCurvePoints.Count == 0
                ? ImmutableSortedDictionary<int, double>.Empty
                : reply.CustomCurvePoints.ToImmutableSortedDictionary(point => point.TemperatureCelsius, point => point.FanDutyPercent),
            DrivingTemperatureAggregation = ParseTemperatureAggregationMode(reply.DrivingTemperatureAggregation),
            DrivingSensorIndices = [.. reply.DrivingSensorIndices],
            TreatMissingSensorsAsZero = reply.TreatMissingSensorsAsZero,
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
            Calibration = ParseCalibration(reply.Calibration),
            AdaptiveSettings = ParseAdaptiveSettings(reply.AdaptiveSettings),
            AdaptiveControl = ParseAdaptiveControl(reply.AdaptiveControl),
            AdaptiveLearning = ParseAdaptiveLearning(reply.AdaptiveLearning),
            ObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(reply.ObservedAtUnixTimeMilliseconds),
            IsAvailable = reply.IsAvailable,
        });
    }

    /// <summary>
    /// Parses a calibration off the wire. Shared with the fan control client, which receives the same message
    /// as the final update of a calibration run.
    /// </summary>
    internal static FanCalibrationSnapshot ParseCalibration(FanCalibrationMessage? message)
        => message is null
            ? FanCalibrationSnapshot.None
            : new FanCalibrationSnapshot
            {
                State = message.State switch
                {
                    FanCalibrationStateValue.Ok => FanCalibrationState.Ok,
                    FanCalibrationStateValue.Stale => FanCalibrationState.Stale,

                    // Mapped explicitly: collapsing it into None would report a fan that is adaptively driven
                    // on the conservative built-in model as having no model at all.
                    FanCalibrationStateValue.Bootstrap => FanCalibrationState.Bootstrap,
                    _ => FanCalibrationState.None,
                },

                // Zero is the proto default for an unset int64, and 1970 is not a calibration date.
                CalibratedAt = message.CalibratedAtUnixTimeMilliseconds > 0L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(message.CalibratedAtUnixTimeMilliseconds)
                    : null,
                ProcessGainCelsiusPerPercent = message.ProcessGainCelsiusPerPercent,
                TimeConstantSeconds = message.TimeConstantSeconds,
                DeadTimeSeconds = message.DeadTimeSeconds,
                MinimumSpinRpm = message.MinimumSpinRpm,
                MinimumSpinDutyPercent = message.MinimumSpinDutyPercent,
                MaximumRpm = message.MaximumRpm,
                ProportionalGain = message.ProportionalGain,
                IntegralGain = message.IntegralGain,
                FeedForwardDutyPerWatt = message.FeedForwardDutyPerWatt,
                PerformanceResponse = message.PerformanceResponse is null
                    ? FanPerformanceResponse.None
                    : new FanPerformanceResponse
                    {
                        LowDutyPercent = message.PerformanceResponse.LowDutyPercent,
                        FullDutyPercent = message.PerformanceResponse.FullDutyPercent,

                        // Has-checks throughout: a proto3 optional defaults to zero, and "0 MHz" reported as
                        // a measurement would show the user a fan that stopped the GPU dead.
                        CpuPerformanceRatioAtLowDuty = message.PerformanceResponse.HasCpuPerformanceRatioAtLowDuty
                            ? message.PerformanceResponse.CpuPerformanceRatioAtLowDuty
                            : null,
                        CpuPerformanceRatioAtFullDuty = message.PerformanceResponse.HasCpuPerformanceRatioAtFullDuty
                            ? message.PerformanceResponse.CpuPerformanceRatioAtFullDuty
                            : null,
                        GpuCoreClockAtLowDutyMegahertz = message.PerformanceResponse.HasGpuCoreClockAtLowDutyMegahertz
                            ? message.PerformanceResponse.GpuCoreClockAtLowDutyMegahertz
                            : null,
                        GpuCoreClockAtFullDutyMegahertz = message.PerformanceResponse.HasGpuCoreClockAtFullDutyMegahertz
                            ? message.PerformanceResponse.GpuCoreClockAtFullDutyMegahertz
                            : null,
                    },
                GainCurve = message.GainCurvePoints.Count == 0
                    ? FanGainCurve.None
                    : new FanGainCurve
                    {
                        Points =
                        [
                            .. message.GainCurvePoints
                                .OrderBy(static point => point.DutyPercent)
                                .Select(static point => new FanGainPoint(point.DutyPercent, point.SettledCelsius)),
                        ],
                    },
            };

    private static AdaptiveFanSettings ParseAdaptiveSettings(AdaptiveFanSettingsMessage? message)
        => message is null
            ? AdaptiveFanSettings.Default
            : new AdaptiveFanSettings
            {
                TargetTemperatureCelsius = message.TargetTemperatureCelsius,
                SafetyFloorEnabled = message.SafetyFloorEnabled,
                SafetyFloorPercent = message.SafetyFloorPercent,
                // 0 is an older service's unset proto default, not a chosen pace.
                LambdaSeconds = message.LambdaSeconds > 0d
                    ? message.LambdaSeconds
                    : Services.Control.AdaptivePidTuning.DefaultLambdaSeconds,
            };

    private static AdaptiveControlDecision? ParseAdaptiveControl(AdaptiveControlMessage? message)
        => message is null
            ? null
            : new AdaptiveControlDecision
            {
                IsDriven = true,
                FeedForwardDutyPercent = message.FeedForwardDutyPercent,
                ProportionalIntegralDutyPercent = message.ProportionalIntegralDutyPercent,
                LeadDutyPercent = message.LeadDutyPercent,
                ThrottleEscalationDutyPercent = message.ThrottleEscalationDutyPercent,
                RawDutyPercent = message.RawDutyPercent,
                DutyPercent = message.DutyPercent,
                ExpectedRpm = message.HasExpectedRpm ? message.ExpectedRpm : null,
                DrivingTemperatureCelsius = message.DrivingTemperatureCelsius,
                TargetTemperatureCelsius = message.TargetTemperatureCelsius,
                IsThrottleLatched = message.IsThrottleLatched,
                ThrottleLatchedAt = message.ThrottleLatchedAtUnixTimeMilliseconds > 0L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(message.ThrottleLatchedAtUnixTimeMilliseconds)
                    : null,
                ThrottleLatchReleaseSeconds = message.HasThrottleLatchReleaseSeconds ? message.ThrottleLatchReleaseSeconds : null,
                IsFeedForwardUnavailable = message.IsFeedForwardUnavailable,
            };

    private static AdaptiveLearningState ParseAdaptiveLearning(AdaptiveLearningMessage? message)
        => message is null
            ? AdaptiveLearningState.None
            : new AdaptiveLearningState
            {
                FeedForwardDutyPerWatt = message.HasFeedForwardDutyPerWatt ? message.FeedForwardDutyPerWatt : null,
                CalibratedAnchorDutyPerWatt = message.HasCalibratedAnchorDutyPerWatt ? message.CalibratedAnchorDutyPerWatt : null,
                IdentifiedProcessGainCelsiusPerPercent = message.HasIdentifiedProcessGainCelsiusPerPercent
                    ? message.IdentifiedProcessGainCelsiusPerPercent
                    : null,
                IdentifiedCelsiusPerWatt = message.HasIdentifiedCelsiusPerWatt ? message.IdentifiedCelsiusPerWatt : null,
                ObservationCount = message.ObservationCount,
                LastUpdatedAt = message.LastUpdatedAtUnixTimeMilliseconds > 0L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(message.LastUpdatedAtUnixTimeMilliseconds)
                    : null,
                LastMaterialChangeAt = message.LastMaterialChangeAtUnixTimeMilliseconds > 0L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(message.LastMaterialChangeAtUnixTimeMilliseconds)
                    : null,
                GainHistory =
                [
                    .. message.GainHistory
                        .Where(static sample => sample.AtUnixTimeMilliseconds > 0L)
                        .Select(static sample => new AdaptiveGainSample(
                            DateTimeOffset.FromUnixTimeMilliseconds(sample.AtUnixTimeMilliseconds),
                            sample.ProcessGainCelsiusPerPercent)),
                ],
            };

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
            TreatMissingSensorsAsZero = reply.TreatMissingSensorsAsZero,
        };
    }

    private static FanControlMode ParseFanControlMode(FanControlModeValue value)
    {
        return value switch
        {
            FanControlModeValue.Auto => FanControlMode.Auto,
            FanControlModeValue.Manual => FanControlMode.Manual,
            FanControlModeValue.CustomCurve => FanControlMode.CustomCurve,
            FanControlModeValue.Adaptive => FanControlMode.Adaptive,
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
