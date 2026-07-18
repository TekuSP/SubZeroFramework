using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkFanControlGrpcService : FrameworkFanControlService.FrameworkFanControlServiceBase
{
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly FrameworkServiceConfigurationStore _configurationStore;
    private readonly FanPreviewWatchdog _previewWatchdog;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<FrameworkFanControlGrpcService> _logger;

    public FrameworkFanControlGrpcService(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlAuthorizationService authorizationService,
        FrameworkFanControlStateStore fanControlStateStore,
        FrameworkServiceConfigurationStore configurationStore,
        FanPreviewWatchdog previewWatchdog,
        IHostApplicationLifetime applicationLifetime,
        ILogger<FrameworkFanControlGrpcService> logger)
    {
        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _fanControlStateStore = fanControlStateStore;
        _configurationStore = configurationStore;
        _previewWatchdog = previewWatchdog;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public override async Task<SetFanRpmReply> SetFanRpm(SetFanRpmRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanRpm command for fan {FanIndex} with target {TargetSpeedRpm} RPM.", request.FanIndex, request.TargetSpeedRpm);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanRpmAsync(request.FanIndex, request.TargetSpeedRpm, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkManual(request.FanIndex);
            await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied SetFanRpm command for fan {FanIndex}. AppliedSpeedRpm={AppliedSpeedRpm}.", result.FanIndex, result.AppliedSpeedRpm);
            return new SetFanRpmReply
            {
                FanIndex = result.FanIndex,
                AppliedSpeedRpm = result.AppliedSpeedRpm,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanRpm command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanRpm command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<SetFanDutyReply> SetFanDuty(SetFanDutyRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanDuty command for fan {FanIndex} with target duty {DutyPercent}% (preview={Preview}).", request.FanIndex, request.DutyPercent, request.Preview);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanDutyAsync(request.FanIndex, request.DutyPercent, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkManual(request.FanIndex);
            _fanControlStateStore.RecordAppliedDuty(request.FanIndex, result.AppliedDutyPercent);
            await PersistFanControlStateAsync(request.FanIndex, request.Preview, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied SetFanDuty command for fan {FanIndex}. AppliedDutyPercent={AppliedDutyPercent}.", result.FanIndex, result.AppliedDutyPercent);
            return new SetFanDutyReply
            {
                FanIndex = result.FanIndex,
                AppliedDutyPercent = result.AppliedDutyPercent,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanDuty command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanDuty command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<SetFanMaxReply> SetFanMax(SetFanMaxRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanMax command for fan {FanIndex} (preview={Preview}).", request.FanIndex, request.Preview);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.SetFanDutyAsync(request.FanIndex, 100d, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkMax(request.FanIndex);
            _fanControlStateStore.RecordAppliedDuty(request.FanIndex, result.AppliedDutyPercent);
            await PersistFanControlStateAsync(request.FanIndex, request.Preview, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied SetFanMax command for fan {FanIndex}. AppliedDutyPercent={AppliedDutyPercent}.", result.FanIndex, result.AppliedDutyPercent);
            return new SetFanMaxReply
            {
                FanIndex = result.FanIndex,
                AppliedDutyPercent = result.AppliedDutyPercent,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanMax command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanMax command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<SetFanCustomCurveReply> SetFanCustomCurve(SetFanCustomCurveRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanCustomCurve command for fan {FanIndex} with {PointCount} points and {SensorCount} driving sensors (preview={Preview}).", request.FanIndex, request.CurvePoints.Count, request.DrivingSensorIndices.Count, request.Preview);
            _authorizationService.EnsureCommandAccess();

            if (request.CurvePoints.Count < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(request.CurvePoints), "Custom fan curve requires at least two points.");
            }

            if (!TelemetryGrpcMapper.TryParseTemperatureAggregationMode(request.DrivingTemperatureAggregation, out var aggregationMode))
            {
                aggregationMode = TemperatureAggregationMode.Average;
            }

            var points = request.CurvePoints.ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var sensors = request.DrivingSensorIndices.ToArray();

            _fanControlStateStore.SetCustomCurve(request.FanIndex, points, aggregationMode, sensors);

            // A preview actuates the EC live (and streams to clients via the in-memory store) but is not
            // written to the configuration store, so it does not survive a service restart. The commit path
            // persists the full BuildFanControlOptions snapshot — hand-building a legacy options object here
            // used to REPLACE the fan's persisted entry, silently wiping its curve profile slots, fan link,
            // and CPU usage modifier from disk.
            await PersistFanControlStateAsync(request.FanIndex, request.Preview, context.CancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Applied SetFanCustomCurve command for fan {FanIndex} (preview={Preview}).", request.FanIndex, request.Preview);

            return new SetFanCustomCurveReply
            {
                FanIndex = request.FanIndex,
                Succeeded = true,
                Message = string.Empty,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanCustomCurve command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanCustomCurve command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<RestoreAutoFanControlReply> RestoreAutoFanControl(RestoreAutoFanControlRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received RestoreAutoFanControl command for fan {FanIndex} (preview={Preview}).", request.FanIndex, request.Preview);
            _authorizationService.EnsureCommandAccess();
            var result = await _frameworkDataProvider.RestoreAutoFanControlAsync(request.FanIndex, context.CancellationToken).ConfigureAwait(false);
            _fanControlStateStore.MarkAuto(request.FanIndex);
            await PersistFanControlStateAsync(request.FanIndex, request.Preview, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied RestoreAutoFanControl command for fan {FanIndex}.", result.FanIndex);
            return new RestoreAutoFanControlReply
            {
                FanIndex = result.FanIndex,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected RestoreAutoFanControl command for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected RestoreAutoFanControl command for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override Task<ChargeLimitsReply> GetChargeLimits(GetChargeLimitsRequest request, ServerCallContext context)
    {
        try
        {
            var limits = _frameworkDataProvider.GetChargeLimits();
            if (limits is null)
            {
                return Task.FromResult(new ChargeLimitsReply
                {
                    Succeeded = false,
                    IsAvailable = false,
                    Message = "Battery charge limits are unavailable.",
                });
            }

            return Task.FromResult(new ChargeLimitsReply
            {
                Succeeded = true,
                IsAvailable = true,
                MinimumPercent = limits.MinimumPercent,
                MaximumPercent = limits.MaximumPercent,
            });
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected GetChargeLimits because the service was not in a readable state.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<ChargeLimitsReply> SetChargeLimits(SetChargeLimitsRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetChargeLimits command (min={Minimum}%, max={Maximum}%).", request.MinimumPercent, request.MaximumPercent);
            _authorizationService.EnsureCommandAccess();
            await _frameworkDataProvider.SetChargeLimitsAsync(request.MinimumPercent, request.MaximumPercent, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied SetChargeLimits command (min={Minimum}%, max={Maximum}%).", request.MinimumPercent, request.MaximumPercent);
            return new ChargeLimitsReply
            {
                Succeeded = true,
                IsAvailable = true,
                MinimumPercent = request.MinimumPercent,
                MaximumPercent = request.MaximumPercent,
            };
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(exception, "Rejected SetChargeLimits command because the request was invalid.");
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetChargeLimits command because the service was not in a writable state.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<FanCurveProfileOperationReply> SaveFanCurveProfile(SaveFanCurveProfileRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SaveFanCurveProfile for fan {FanIndex} slot {Slot} (activate={Activate}, follow={HasFollow}).", request.FanIndex, request.Slot, request.Activate, request.HasFollowTarget);
            _authorizationService.EnsureCommandAccess();
            EnsureSlotInRange(request.Slot);

            if (!TelemetryGrpcMapper.TryParseTemperatureAggregationMode(request.DrivingTemperatureAggregation, out var aggregationMode))
            {
                aggregationMode = TemperatureAggregationMode.Average;
            }

            var followFanIndex = request.HasFollowTarget ? request.FollowFanIndex : (int?)null;

            // A follow slot may carry no points (it mirrors another fan); a self-driven slot needs at least two.
            if (followFanIndex is null && request.CurvePoints.Count < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(request.CurvePoints), "A self-driven custom fan curve requires at least two points.");
            }

            if (followFanIndex == request.FanIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(request.FollowFanIndex), "A fan curve profile cannot follow its own fan.");
            }

            var points = request.CurvePoints.ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var sensors = request.DrivingSensorIndices.ToArray();
            var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name;

            _fanControlStateStore.SaveCurveProfile(request.FanIndex, request.Slot, name, points, aggregationMode, sensors, followFanIndex, request.Activate);
            await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Saved fan curve profile for fan {FanIndex} slot {Slot}.", request.FanIndex, request.Slot);
            return SucceededProfileReply(request.FanIndex, request.Slot);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SaveFanCurveProfile for fan {FanIndex} slot {Slot} because the request was invalid.", request.FanIndex, request.Slot);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SaveFanCurveProfile for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<FanCurveProfileOperationReply> SetActiveFanCurveProfile(SetActiveFanCurveProfileRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetActiveFanCurveProfile for fan {FanIndex} slot {Slot}.", request.FanIndex, request.Slot);
            _authorizationService.EnsureCommandAccess();
            EnsureSlotInRange(request.Slot);

            _fanControlStateStore.SetActiveCurveProfile(request.FanIndex, request.Slot);
            await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);

            return SucceededProfileReply(request.FanIndex, request.Slot);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetActiveFanCurveProfile for fan {FanIndex} slot {Slot} because the request was invalid.", request.FanIndex, request.Slot);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetActiveFanCurveProfile for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<FanCurveProfileOperationReply> ClearFanCurveProfile(ClearFanCurveProfileRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received ClearFanCurveProfile for fan {FanIndex} slot {Slot}.", request.FanIndex, request.Slot);
            _authorizationService.EnsureCommandAccess();
            EnsureSlotInRange(request.Slot);

            _fanControlStateStore.ClearCurveProfile(request.FanIndex, request.Slot);
            await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);

            return SucceededProfileReply(request.FanIndex, request.Slot);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected ClearFanCurveProfile for fan {FanIndex} slot {Slot} because the request was invalid.", request.FanIndex, request.Slot);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected ClearFanCurveProfile for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<FanCurveProfileOperationReply> SetFanLink(SetFanLinkRequest request, ServerCallContext context)
    {
        try
        {
            var leader = request.HasLinkedLeaderIndex ? request.LinkedLeaderIndex : (int?)null;
            _logger.LogInformation("Received SetFanLink for fan {FanIndex} -> leader {Leader}.", request.FanIndex, leader);
            _authorizationService.EnsureCommandAccess();

            if (!_fanControlStateStore.SetLinkedLeader(request.FanIndex, leader))
            {
                return new FanCurveProfileOperationReply
                {
                    FanIndex = request.FanIndex,
                    Succeeded = false,
                    Message = $"Unknown fan {request.FanIndex}.",
                };
            }

            await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);

            return new FanCurveProfileOperationReply { FanIndex = request.FanIndex, Succeeded = true, Message = string.Empty };
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanLink for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task HoldFanPreview(HoldFanPreviewRequest request, IServerStreamWriter<HoldFanPreviewReply> responseStream, ServerCallContext context)
    {
        var fanIndex = request.FanIndex;
        _logger.LogInformation("Opening preview hold for fan {FanIndex}.", fanIndex);

        // Capture the fan's current (applied) state before any preview command mutates it. The client waits
        // for the ready reply below before sending its preview command, so this captures the pre-preview state.
        if (_fanControlStateStore.GetState(fanIndex) is { } prePreview)
        {
            _previewWatchdog.Begin(fanIndex, prePreview);
        }

        try
        {
            await responseStream.WriteAsync(new HoldFanPreviewReply { Ready = true }).ConfigureAwait(false);

            // Hold the stream open until the client closes it (commit / revert / fan switch) or disconnects.
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the client cancels the call or disconnects.
        }
        finally
        {
            // Revert only if the hold is still active (not committed/released by an Apply or client-side
            // restore) and the service is not shutting down (the shutdown coordinator restores fans to Auto).
            if (!_applicationLifetime.ApplicationStopping.IsCancellationRequested
                && _previewWatchdog.TryTakeForRevert(fanIndex, out var snapshot))
            {
                _logger.LogWarning("Preview hold for fan {FanIndex} closed without commit; reverting to the pre-preview state.", fanIndex);
                await RevertPreviewAsync(fanIndex, snapshot, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _previewWatchdog.Release(fanIndex);
                _logger.LogInformation("Closed preview hold for fan {FanIndex} (committed or shutting down).", fanIndex);
            }
        }
    }

    // Restores a fan to its captured pre-preview state after an uncommitted preview hold dropped. Mirrors the
    // commit actuation paths; a curve snapshot is re-published so the curve worker re-actuates the EC.
    private async Task RevertPreviewAsync(int fanIndex, FanControlStateSnapshot prePreview, CancellationToken cancellationToken)
    {
        // Matches the client/service default manual duty when a Manual pre-state never recorded one.
        const double defaultManualDutyPercent = 50d;

        try
        {
            switch (prePreview.Mode)
            {
                case FanControlMode.Manual:
                    var duty = prePreview.LastDutyPercent ?? defaultManualDutyPercent;
                    await _frameworkDataProvider.SetFanDutyAsync(fanIndex, duty, cancellationToken).ConfigureAwait(false);
                    _fanControlStateStore.MarkManual(fanIndex);
                    _fanControlStateStore.RecordAppliedDuty(fanIndex, duty);
                    break;
                case FanControlMode.Max:
                    await _frameworkDataProvider.SetFanDutyAsync(fanIndex, 100d, cancellationToken).ConfigureAwait(false);
                    _fanControlStateStore.MarkMax(fanIndex);
                    break;
                case FanControlMode.CustomCurve:
                    _fanControlStateStore.RestoreState(prePreview);
                    break;
                default:
                    await _frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, cancellationToken).ConfigureAwait(false);
                    _fanControlStateStore.MarkAuto(fanIndex);
                    break;
            }

            _logger.LogInformation("Reverted fan {FanIndex} to its pre-preview state ({Mode}) after the preview hold dropped.", fanIndex, prePreview.Mode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to revert fan {FanIndex} after its preview hold dropped.", fanIndex);
        }
    }

    private static void EnsureSlotInRange(int slot)
    {
        if (slot is < 0 or >= FrameworkFanControlStateStore.MaxCurveProfileSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"Curve profile slot must be between 0 and {FrameworkFanControlStateStore.MaxCurveProfileSlots - 1}.");
        }
    }

    public override async Task<SetFanUsageModifierReply> SetFanUsageModifier(SetFanUsageModifierRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanUsageModifier for fan {FanIndex}. CpuStrength={CpuStrength}.", request.FanIndex, request.CpuUsageModifierStrength);
            _authorizationService.EnsureCommandAccess();

            // Persisting this command snapshots the fan's live in-memory state — during an open preview hold
            // that state contains the uncommitted preview, so persisting would commit the preview and release
            // the watchdog that exists to revert it. Reject instead; the client applies the modifier after
            // committing or discarding the preview (matching the page's Stage -> Preview -> Apply model).
            if (_previewWatchdog.HasOpenHold(request.FanIndex))
            {
                throw new InvalidOperationException($"Fan {request.FanIndex} has a live preview open. Apply or discard the preview before changing its usage modifier.");
            }

            // NaN is the wire encoding for "disabled"; anything else must be a sane duty-point strength.
            double? strength = double.IsNaN(request.CpuUsageModifierStrength) ? null : request.CpuUsageModifierStrength;
            if (strength is double value && (!double.IsFinite(value) || value is < 0d or > 100d))
            {
                throw new ArgumentOutOfRangeException(nameof(request.CpuUsageModifierStrength), "The CPU usage modifier strength must be between 0 and 100 duty points, or NaN to disable it.");
            }

            if (!_fanControlStateStore.SetCpuUsageModifier(request.FanIndex, strength))
            {
                throw new ArgumentOutOfRangeException(nameof(request.FanIndex), $"Fan {request.FanIndex} is not known to the fan control state store.");
            }

            await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Applied SetFanUsageModifier for fan {FanIndex}. CpuStrength={CpuStrength}.", request.FanIndex, strength);
            return new SetFanUsageModifierReply
            {
                FanIndex = request.FanIndex,
                Succeeded = true,
                Message = string.Empty,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanUsageModifier for fan {FanIndex} because the request was invalid.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanUsageModifier for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    private static FanCurveProfileOperationReply SucceededProfileReply(int fanIndex, int slot)
        => new() { FanIndex = fanIndex, Slot = slot, Succeeded = true, Message = string.Empty };

    private async Task PersistFanControlStateAsync(int fanIndex, bool preview, CancellationToken cancellationToken)
    {
        // A preview is volatile: the EC and the in-memory store reflect it (so live clients see it), but it
        // is never written to the configuration store. A service restart therefore restores the last applied
        // state, and "Apply" simply re-sends the same command with preview=false to persist it.
        if (preview)
        {
            return;
        }

        // A persisting command commits (or restores) the fan, so any open preview hold must not later revert it.
        _previewWatchdog.Release(fanIndex);

        var options = _fanControlStateStore.BuildFanControlOptions(fanIndex);
        if (options is null)
        {
            return;
        }

        try
        {
            await _configurationStore.UpsertFanControlStateAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception persistenceException)
        {
            _logger.LogWarning(persistenceException, "Saved fan curve profiles for fan {FanIndex} in memory but failed to persist them. They will not survive a service restart.", fanIndex);
        }
    }
}
