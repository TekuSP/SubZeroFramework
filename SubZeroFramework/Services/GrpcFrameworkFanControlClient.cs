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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curvePoints);
        ArgumentNullException.ThrowIfNull(drivingSensorIndices);

        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var request = new SetFanCustomCurveRequest
        {
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

    public async Task<FrameworkFanUsageModifierCommandResult> SetUsageModifierAsync(int fanIndex, double? cpuUsageModifierStrength, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = _channelFactory.CreateTimeoutCancellationSource(cancellationToken);
        var reply = await _client.SetFanUsageModifierAsync(new SetFanUsageModifierRequest
        {
            FanIndex = fanIndex,
            // NaN is the wire encoding for "disabled".
            CpuUsageModifierStrength = cpuUsageModifierStrength ?? double.NaN,
        }, cancellationToken: timeoutSource.Token).ResponseAsync.ConfigureAwait(false);

        return new FrameworkFanUsageModifierCommandResult
        {
            FanIndex = reply.FanIndex,
            Succeeded = reply.Succeeded,
            Message = reply.Message ?? string.Empty,
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
