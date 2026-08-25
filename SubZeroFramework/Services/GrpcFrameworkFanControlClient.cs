using Grpc.Core;

using SubZeroFramework.GrpcContracts;

namespace SubZeroFramework.Services;

public sealed class GrpcFrameworkFanControlClient : IFrameworkFanControlClient
{
    private readonly FrameworkGrpcChannelFactory _channelFactory;
    private readonly FrameworkFanControlService.FrameworkFanControlServiceClient _client;

    public GrpcFrameworkFanControlClient(FrameworkGrpcChannelFactory channelFactory)
    {
        ArgumentNullException.ThrowIfNull(channelFactory);

        _channelFactory = channelFactory;
        _client = new FrameworkFanControlService.FrameworkFanControlServiceClient(_channelFactory.Channel);
    }

    /// <summary>
    /// Sets the fan speed target in RPM.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="targetSpeedRpm">The requested fan speed in RPM.</param>
    public async Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetFanRpmAsync(new SetFanRpmRequest
        {
            FanIndex = fanIndex,
            TargetSpeedRpm = targetSpeedRpm,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanRpmCommandResult
        {
            FanIndex = reply.FanIndex,
            AppliedSpeedRpm = reply.AppliedSpeedRpm,
        };
    }

    /// <summary>
    /// Sets the fan duty cycle percent.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="dutyPercent">The requested duty cycle percent.</param>
    /// <param name="preview">When true, actuate the EC live without persisting the override (a volatile preview).</param>
    public async Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, bool preview = false, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetFanDutyAsync(new SetFanDutyRequest
        {
            FanIndex = fanIndex,
            DutyPercent = dutyPercent,
            Preview = preview,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanDutyCommandResult
        {
            FanIndex = reply.FanIndex,
            AppliedDutyPercent = reply.AppliedDutyPercent,
        };
    }

    /// <summary>
    /// Forces the fan to 100% duty (Max mode).
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="preview">When true, actuate the EC live without persisting the override (a volatile preview).</param>
    public async Task<FrameworkFanMaxCommandResult> SetFanMaxAsync(int fanIndex, bool preview = false, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetFanMaxAsync(new SetFanMaxRequest
        {
            FanIndex = fanIndex,
            Preview = preview,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanMaxCommandResult
        {
            FanIndex = reply.FanIndex,
            AppliedDutyPercent = reply.AppliedDutyPercent,
        };
    }

    public async Task<FrameworkFanCustomCurveCommandResult> SetCustomCurveAsync(
        int fanIndex,
        IReadOnlyDictionary<int, double> curvePoints,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregationMode,
        bool preview = false,
        bool treatMissingSensorsAsZero = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var request = new SetFanCustomCurveRequest
        {
            TreatMissingSensorsAsZero = treatMissingSensorsAsZero,
            FanIndex = fanIndex,
            DrivingTemperatureAggregation = MapAggregationMode(aggregationMode),
            Preview = preview,
        };

        foreach (var pair in curvePoints)
        {
            request.CurvePoints[pair.Key] = pair.Value;
        }

        foreach (var sensorIndex in drivingSensorIndices)
        {
            request.DrivingSensorIndices.Add(sensorIndex);
        }

        var reply = await _client.SetFanCustomCurveAsync(request, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanCustomCurveCommandResult
        {
            FanIndex = reply.FanIndex,
            Succeeded = reply.Succeeded,
            Message = reply.Message ?? string.Empty,
        };
    }

    private static TemperatureAggregationModeValue MapAggregationMode(TemperatureAggregationMode mode)
    {
        return mode switch
        {
            TemperatureAggregationMode.Median => TemperatureAggregationModeValue.Median,
            TemperatureAggregationMode.Maximum => TemperatureAggregationModeValue.Maximum,
            TemperatureAggregationMode.Minimum => TemperatureAggregationModeValue.Minimum,
            _ => TemperatureAggregationModeValue.Average,
        };
    }

    /// <summary>
    /// Restores automatic fan control for the specified fan.
    /// </summary>
    /// <param name="fanIndex">The zero-based fan index.</param>
    /// <param name="preview">When true, actuate the EC live without persisting the change (a volatile preview).</param>
    public async Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, bool preview = false, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.RestoreAutoFanControlAsync(new RestoreAutoFanControlRequest
        {
            FanIndex = fanIndex,
            Preview = preview,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkRestoreAutoFanControlCommandResult
        {
            FanIndex = reply.FanIndex,
        };
    }

    public async Task<FrameworkFanCurveProfileCommandResult> SaveCurveProfileAsync(
        int fanIndex,
        int slot,
        string? name,
        IReadOnlyDictionary<int, double> curvePoints,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregationMode,
        int? followFanIndex,
        bool activate,
        bool treatMissingSensorsAsZero = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var request = new SaveFanCurveProfileRequest
        {
            FanIndex = fanIndex,
            Slot = slot,
            Name = name ?? string.Empty,
            DrivingTemperatureAggregation = MapAggregationMode(aggregationMode),
            HasFollowTarget = followFanIndex is not null,
            FollowFanIndex = followFanIndex ?? 0,
            Activate = activate,
            TreatMissingSensorsAsZero = treatMissingSensorsAsZero,
        };

        foreach (var pair in curvePoints)
        {
            request.CurvePoints[pair.Key] = pair.Value;
        }

        foreach (var sensorIndex in drivingSensorIndices)
        {
            request.DrivingSensorIndices.Add(sensorIndex);
        }

        var reply = await _client.SaveFanCurveProfileAsync(request, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapProfileReply(reply);
    }

    public async Task<FrameworkFanCurveProfileCommandResult> SetActiveCurveProfileAsync(int fanIndex, int slot, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetActiveFanCurveProfileAsync(new SetActiveFanCurveProfileRequest
        {
            FanIndex = fanIndex,
            Slot = slot,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return MapProfileReply(reply);
    }

    public async Task<FrameworkFanCurveProfileCommandResult> ClearCurveProfileAsync(int fanIndex, int slot, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.ClearFanCurveProfileAsync(new ClearFanCurveProfileRequest
        {
            FanIndex = fanIndex,
            Slot = slot,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return MapProfileReply(reply);
    }

    public async Task<FrameworkFanCurveProfileCommandResult> SetFanLinkAsync(int fanIndex, int? linkedLeaderIndex, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var request = new SetFanLinkRequest { FanIndex = fanIndex };
        if (linkedLeaderIndex is int leader)
        {
            request.LinkedLeaderIndex = leader;
        }

        var reply = await _client.SetFanLinkAsync(request, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapProfileReply(reply);
    }

    public async Task<FrameworkFanCurveProfileCommandResult> SetAdaptiveModeAsync(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        TemperatureAggregationMode aggregation,
        AdaptiveFanSettings? settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var request = new SetFanAdaptiveModeRequest
        {
            FanIndex = fanIndex,
            DrivingTemperatureAggregation = MapAggregationMode(aggregation),
        };

        request.DrivingSensorIndices.AddRange(drivingSensorIndices);

        if (settings is not null)
        {
            request.Settings = ToMessage(settings);
        }

        var reply = await _client.SetFanAdaptiveModeAsync(request, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);
        return MapProfileReply(reply);
    }

    public async Task<FrameworkFanCurveProfileCommandResult> SetAdaptiveSettingsAsync(
        int fanIndex,
        AdaptiveFanSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetFanAdaptiveSettingsAsync(
            new SetFanAdaptiveSettingsRequest { FanIndex = fanIndex, Settings = ToMessage(settings) },
            cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return MapProfileReply(reply);
    }

    public async Task<FrameworkFanCurveProfileCommandResult> ReleaseThrottleLatchAsync(int fanIndex, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.ReleaseFanThrottleLatchAsync(
            new ReleaseFanThrottleLatchRequest { FanIndex = fanIndex },
            cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return MapProfileReply(reply);
    }

    public async Task<FrameworkFanCurveProfileCommandResult> ForgetAdaptiveLearningAsync(int fanIndex, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.ForgetFanAdaptiveLearningAsync(
            new ForgetFanAdaptiveLearningRequest { FanIndex = fanIndex },
            cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return MapProfileReply(reply);
    }

    private static AdaptiveFanSettingsMessage ToMessage(AdaptiveFanSettings settings)
        => new()
        {
            TargetTemperatureCelsius = settings.TargetTemperatureCelsius,
            SafetyFloorEnabled = settings.SafetyFloorEnabled,
            SafetyFloorPercent = settings.SafetyFloorPercent,
        };

    public async Task<FrameworkFanControlResetCommandResult> ResetFanControlToFactoryDefaultsAsync(CancellationToken cancellationToken = default)
    {
        // The standard unary deadline covers this: the service restores a handful of fans on the EC and
        // performs a single configuration write, all well inside the timeout.
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.ResetFanControlToFactoryDefaultsAsync(
            new ResetFanControlToFactoryDefaultsRequest(),
            cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanControlResetCommandResult
        {
            Succeeded = reply.Succeeded,
            Message = reply.Message ?? string.Empty,
            FansRestored = reply.FansRestored,
            FansFailed = reply.FansFailed,
            PersistedEntriesCleared = reply.PersistedEntriesCleared,
        };
    }

    public async Task OpenPreviewHoldAsync(int fanIndex, CancellationToken cancellationToken)
    {
        // A long-lived safety lease, so it is NOT wrapped in the unary timeout source — it must stay open for
        // the whole preview. The caller's token closes it (commit / revert / app exit), which the service
        // observes to revert the fan if the preview was never committed.
        var call = _client.HoldFanPreview(new HoldFanPreviewRequest { FanIndex = fanIndex }, cancellationToken: cancellationToken);

        // Await the first "ready" reply so the pre-preview state is captured before the caller previews.
        await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);

        // Keep draining (and dispose the call) in the background until the token is cancelled.
        _ = DrainPreviewHoldAsync(call, cancellationToken);
    }

    public async Task<FanCalibrationRunResult> RunCalibrationAsync(
        int fanIndex,
        IReadOnlyCollection<int> drivingSensorIndices,
        IProgress<FanCalibrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        var request = new RunFanCalibrationRequest { FanIndex = fanIndex };
        request.DrivingSensorIndices.AddRange(drivingSensorIndices);

        // Deliberately NOT wrapped in the unary timeout source. A calibration runs for minutes by design, and
        // the shared deadline would kill it mid-test — leaving the service to abort a run the user was
        // watching succeed.
        using var call = _client.RunFanCalibration(request, cancellationToken: cancellationToken);

        FanCalibrationProgressReply? final = null;

        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            var reply = call.ResponseStream.Current;

            if (reply.IsComplete)
            {
                final = reply;
                continue;
            }

            progress?.Report(ParseCalibrationProgress(reply));
        }

        if (final is null)
        {
            // The stream ended without the final message — the service died, or the connection dropped. The
            // run is over either way, and the one thing the caller must not be told is that the fan was put
            // back, because nothing here observed that happening.
            return new FanCalibrationRunResult
            {
                FanIndex = fanIndex,
                Succeeded = false,
                Failure = FanCalibrationFailure.ClientDisconnected,
                FansRestored = false,
            };
        }

        return ParseCalibrationResult(final);
    }

    private static FanCalibrationProgress ParseCalibrationProgress(FanCalibrationProgressReply reply) => new()
    {
        FanIndex = reply.FanIndex,
        Step = ParseCalibrationStep(reply.Step),
        ElapsedSeconds = reply.ElapsedSeconds,

        // Has-checks rather than raw reads: proto3 optionals default to 0, and "0 C" plotted as a reading
        // where the truth is "no reading" would draw a cliff that never happened.
        TemperatureCelsius = reply.HasTemperatureCelsius ? reply.TemperatureCelsius : null,
        SpeedRpm = reply.HasSpeedRpm ? reply.SpeedRpm : null,
        PackagePowerWatts = reply.HasPackagePowerWatts ? reply.PackagePowerWatts : null,
        EstimatedRemaining = reply.HasEstimatedRemainingMilliseconds
            ? TimeSpan.FromMilliseconds(reply.EstimatedRemainingMilliseconds)
            : null,
        IsStepMarker = reply.IsStepMarker,
    };

    private static FanCalibrationRunResult ParseCalibrationResult(FanCalibrationProgressReply reply) => new()
    {
        FanIndex = reply.FanIndex,
        Succeeded = reply.Succeeded,
        Calibration = reply.Calibration is null ? null : GrpcFanControlStateClient.ParseCalibration(reply.Calibration),
        Failure = ParseCalibrationFailure(reply.Failure),
        StoppedAt = ParseCalibrationStep(reply.Step),
        AveragePackagePowerWatts = reply.HasAveragePackagePowerWatts ? reply.AveragePackagePowerWatts : null,
        TemperatureSwingCelsius = reply.HasTemperatureSwingCelsius ? reply.TemperatureSwingCelsius : null,
        PeakTemperatureCelsius = reply.HasPeakTemperatureCelsius ? reply.PeakTemperatureCelsius : null,
        Duration = TimeSpan.FromMilliseconds(reply.DurationMilliseconds),
        FansRestored = reply.FansRestored,
    };

    private static FanCalibrationStep ParseCalibrationStep(FanCalibrationStepValue step) => step switch
    {
        FanCalibrationStepValue.SettlingAtIdle => FanCalibrationStep.SettlingAtIdle,
        FanCalibrationStepValue.FindingMinimumSpin => FanCalibrationStep.FindingMinimumSpin,
        FanCalibrationStepValue.LoadingAndSettling => FanCalibrationStep.LoadingAndSettling,
        FanCalibrationStepValue.SteppingFan => FanCalibrationStep.SteppingFan,
        FanCalibrationStepValue.MeasuringResponse => FanCalibrationStep.MeasuringResponse,
        FanCalibrationStepValue.FittingModel => FanCalibrationStep.FittingModel,
        FanCalibrationStepValue.VerifyingSpeedTracking => FanCalibrationStep.VerifyingSpeedTracking,
        FanCalibrationStepValue.Completed => FanCalibrationStep.Completed,
        _ => FanCalibrationStep.None,
    };

    private static FanCalibrationFailure ParseCalibrationFailure(FanCalibrationFailureValue failure) => failure switch
    {
        FanCalibrationFailureValue.InsufficientLoad => FanCalibrationFailure.InsufficientLoad,
        FanCalibrationFailureValue.InsufficientTemperatureSwing => FanCalibrationFailure.InsufficientTemperatureSwing,
        FanCalibrationFailureValue.TemperatureCeiling => FanCalibrationFailure.TemperatureCeiling,
        FanCalibrationFailureValue.Cancelled => FanCalibrationFailure.Cancelled,
        FanCalibrationFailureValue.ClientDisconnected => FanCalibrationFailure.ClientDisconnected,
        FanCalibrationFailureValue.InsufficientData => FanCalibrationFailure.InsufficientData,
        FanCalibrationFailureValue.OnBattery => FanCalibrationFailure.OnBattery,
        FanCalibrationFailureValue.GpuLoadUnavailable => FanCalibrationFailure.GpuLoadUnavailable,
        _ => FanCalibrationFailure.None,
    };

    private static async Task DrainPreviewHoldAsync(Grpc.Core.AsyncServerStreamingCall<HoldFanPreviewReply> call, CancellationToken cancellationToken)
    {
        try
        {
            while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                // Keepalive replies, if any; nothing to do.
            }
        }
        catch (Exception)
        {
            // Cancellation / disconnect ends the hold — the service handles the revert.
        }
        finally
        {
            call.Dispose();
        }
    }

    private static FrameworkFanCurveProfileCommandResult MapProfileReply(FanCurveProfileOperationReply reply)
        => new()
        {
            FanIndex = reply.FanIndex,
            Slot = reply.Slot,
            Succeeded = reply.Succeeded,
            Message = reply.Message ?? string.Empty,
        };

    public async Task<FrameworkChargeLimitsResult> GetChargeLimitsAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.GetChargeLimitsAsync(new GetChargeLimitsRequest(), cancellationToken: timeoutSource.Token)
            .ResponseAsync.ConfigureAwait(false);

        return MapChargeLimitsReply(reply);
    }

    public async Task<FrameworkChargeLimitsResult> SetChargeLimitsAsync(int minimumPercent, int maximumPercent, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetChargeLimitsAsync(new SetChargeLimitsRequest
        {
            MinimumPercent = minimumPercent,
            MaximumPercent = maximumPercent,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return MapChargeLimitsReply(reply);
    }

    private static FrameworkChargeLimitsResult MapChargeLimitsReply(ChargeLimitsReply reply)
        => new()
        {
            IsAvailable = reply.IsAvailable,
            Succeeded = reply.Succeeded,
            Message = reply.Message ?? string.Empty,
            MinimumPercent = reply.MinimumPercent,
            MaximumPercent = reply.MaximumPercent,
        };
}
