using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Services.Control;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkFanControlGrpcService : FrameworkFanControlService.FrameworkFanControlServiceBase
{
    /// <summary>
    /// Upper bound on curve points accepted from a client. Comfortably above anything the editor can produce
    /// (one point per editable degree would be ~110) while keeping per-evaluation work bounded.
    /// </summary>
    private const int MaxCurvePoints = 256;

    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlStateStore _fanControlStateStore;
    private readonly FrameworkServiceConfigurationStore _configurationStore;
    private readonly FanPreviewWatchdog _previewWatchdog;
    private readonly FanAdaptiveControlSignals _fanControlWorkerSignals;
    private readonly FanCalibrationRunner _calibrationRunner;
    private readonly FanCalibrationArbiter _calibrationArbiter;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<FrameworkFanControlGrpcService> _logger;

    public FrameworkFanControlGrpcService(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlAuthorizationService authorizationService,
        FrameworkFanControlStateStore fanControlStateStore,
        FrameworkServiceConfigurationStore configurationStore,
        FanPreviewWatchdog previewWatchdog,
        FanAdaptiveControlSignals fanControlWorkerSignals,
        FanCalibrationRunner calibrationRunner,
        FanCalibrationArbiter calibrationArbiter,
        IHostApplicationLifetime applicationLifetime,
        ILogger<FrameworkFanControlGrpcService> logger)
    {
        _calibrationArbiter = calibrationArbiter;
        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _fanControlStateStore = fanControlStateStore;
        _configurationStore = configurationStore;
        _previewWatchdog = previewWatchdog;
        _fanControlWorkerSignals = fanControlWorkerSignals;
        _calibrationRunner = calibrationRunner;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public override async Task<SetFanRpmReply> SetFanRpm(SetFanRpmRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanRpm command for fan {FanIndex} with target {TargetSpeedRpm} RPM.", request.FanIndex, request.TargetSpeedRpm);
            _authorizationService.EnsureCommandAccess();
            EnsureNotCalibrating(request.FanIndex);
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
            EnsureNotCalibrating(request.FanIndex);
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
            EnsureNotCalibrating(request.FanIndex);
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
            EnsureNotCalibrating(request.FanIndex);

            if (request.CurvePoints.Count < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(request.CurvePoints), "Custom fan curve requires at least two points.");
            }

            // Point count, temperature range and duty range were previously unchecked, so a client could send
            // anything the wire format allowed. Three concrete consequences, all reachable:
            //
            //   * A point at or above FanCurveDomain.MaxTemperatureCelsius suppresses the implicit 100% anchor
            //     (see BuildAnchoredSeries), so {0:0, 1000000:0} evaluated to 0% at EVERY temperature — a fan
            //     pinned off with no thermal backstop but the firmware's own critical shutdown.
            //   * A non-finite duty survives Math.Clamp unchanged (NaN compares false against both bounds), then
            //     throws inside SetFanDutyAsync once per fan per evaluation, forever.
            //   * An unbounded point count is unbounded work per evaluation on the EC write path.
            //
            // The UI already constrains all three; this is the boundary saying so rather than trusting it.
            if (request.CurvePoints.Count > MaxCurvePoints)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.CurvePoints),
                    $"Custom fan curve accepts at most {MaxCurvePoints} points.");
            }

            foreach (var (temperature, duty) in request.CurvePoints)
            {
                if (temperature < FanCurveDomain.EditableMinTemperatureCelsius
                    || temperature > FanCurveDomain.EditableMaxTemperatureCelsius)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(request.CurvePoints),
                        $"Curve temperatures must be between {FanCurveDomain.EditableMinTemperatureCelsius} and {FanCurveDomain.EditableMaxTemperatureCelsius} °C; got {temperature}.");
                }

                if (!double.IsFinite(duty)
                    || duty < FanCurveDomain.MinSpeedDutyPercent
                    || duty > FanCurveDomain.MaxSpeedDutyPercent)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(request.CurvePoints),
                        $"Curve duties must be a finite value between {FanCurveDomain.MinSpeedDutyPercent} and {FanCurveDomain.MaxSpeedDutyPercent}; got {duty}.");
                }
            }

            if (!TelemetryGrpcMapper.TryParseTemperatureAggregationMode(request.DrivingTemperatureAggregation, out var aggregationMode))
            {
                aggregationMode = TemperatureAggregationMode.Average;
            }

            var points = request.CurvePoints.ToDictionary(static pair => pair.Key, static pair => pair.Value);
            var sensors = request.DrivingSensorIndices.ToArray();

            _fanControlStateStore.SetCustomCurve(request.FanIndex, points, aggregationMode, sensors, request.TreatMissingSensorsAsZero);

            // A preview actuates the EC live (and streams to clients via the in-memory store) but is not
            // written to the configuration store, so it does not survive a service restart. The commit path
            // persists the full BuildFanControlOptions snapshot — hand-building a legacy options object here
            // used to REPLACE the fan's persisted entry, silently wiping its curve profile slots and fan link
            // from disk.
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
            EnsureNotCalibrating(request.FanIndex);
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

            _fanControlStateStore.SaveCurveProfile(request.FanIndex, request.Slot, name, points, aggregationMode, sensors, followFanIndex, request.Activate, request.TreatMissingSensorsAsZero);
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

    public override async Task<FanCurveProfileOperationReply> SetFanAdaptiveMode(SetFanAdaptiveModeRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation(
                "Received SetFanAdaptiveMode for fan {FanIndex} with {SensorCount} driving sensor(s) (preview={Preview}).",
                request.FanIndex,
                request.DrivingSensorIndices.Count,
                request.Preview);

            _authorizationService.EnsureCommandAccess();
            EnsureNotCalibrating(request.FanIndex);

            // Deliberately NO open-hold guard, in either direction. A guard here once refused the
            // PERSISTING arm while a preview hold was open — but stage → preview → apply always arrives
            // with the hold open, so Apply threw, the commit that releases the hold never ran, and closing
            // the hold reverted the fan to its captured pre-preview state: a user who applied Adaptive
            // watched their fan land on Auto. The commit contract is the one every other mode follows:
            // PersistFanControlStateAsync(preview: false) releases the hold, which IS the commit.
            var settings = request.Settings is null ? null : ToAdaptiveSettings(request.Settings);

            var result = _fanControlStateStore.SetAdaptiveMode(
                request.FanIndex,
                [.. request.DrivingSensorIndices],
                ParseAggregation(request.DrivingTemperatureAggregation),
                settings);

            if (!result.Succeeded)
            {
                // A deliberate non-throwing failure: "this fan is not calibrated" is an expected, actionable
                // state the UI turns into a Calibrate call to action, not an exception to surface as an error.
                return new FanCurveProfileOperationReply
                {
                    FanIndex = request.FanIndex,
                    Succeeded = false,
                    Message = result.Message,
                };
            }

            var persisted = await PersistFanControlStateAsync(request.FanIndex, request.Preview, context.CancellationToken).ConfigureAwait(false);

            return new FanCurveProfileOperationReply
            {
                FanIndex = request.FanIndex,
                Succeeded = true,
                Message = persisted ? string.Empty : PersistenceFailedWarning,
            };
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanAdaptiveMode for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<FanCurveProfileOperationReply> SetFanAdaptiveSettings(SetFanAdaptiveSettingsRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received SetFanAdaptiveSettings for fan {FanIndex}.", request.FanIndex);
            _authorizationService.EnsureCommandAccess();

            // No open-hold guard, for the same reason SetFanAdaptiveMode has none: applying staged settings
            // through an open preview IS the commit, and persisting releases the hold like every other mode.
            if (request.Settings is null)
            {
                throw new ArgumentException("Adaptive settings are required.", nameof(request));
            }

            if (!_fanControlStateStore.SetAdaptiveSettings(request.FanIndex, ToAdaptiveSettings(request.Settings)))
            {
                return new FanCurveProfileOperationReply
                {
                    FanIndex = request.FanIndex,
                    Succeeded = false,
                    Message = $"Unknown fan {request.FanIndex}.",
                };
            }

            var persisted = await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);

            return new FanCurveProfileOperationReply
            {
                FanIndex = request.FanIndex,
                Succeeded = true,
                Message = persisted ? string.Empty : PersistenceFailedWarning,
            };
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetFanAdaptiveSettings for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override Task<FanCurveProfileOperationReply> ReleaseFanThrottleLatch(ReleaseFanThrottleLatchRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Received ReleaseFanThrottleLatch for fan {FanIndex}.", request.FanIndex);

        // Mapped, not left to escape: EnsureCommandAccess throws InvalidOperationException, and outside a
        // catch that reaches the client as StatusCode.Unknown — indistinguishable from a service crash —
        // instead of the FailedPrecondition every other handler answers with.
        try
        {
            _authorizationService.EnsureCommandAccess();
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected ReleaseFanThrottleLatch for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }

        // Nothing is persisted and no EC write happens here — the worker's controller simply drops its latch
        // on the next tick. That is also why there is no preview-hold guard: this touches no stored state.
        var isAdaptive = _fanControlStateStore.GetState(request.FanIndex)?.Mode == FanControlMode.Adaptive;
        if (isAdaptive)
        {
            _fanControlWorkerSignals.RequestThrottleLatchRelease(request.FanIndex);
        }

        return Task.FromResult(new FanCurveProfileOperationReply
        {
            FanIndex = request.FanIndex,
            Succeeded = isAdaptive,
            Message = isAdaptive ? string.Empty : $"Fan {request.FanIndex} is not running an adaptive controller.",
        });
    }

    public override async Task<FanCurveProfileOperationReply> ForgetFanAdaptiveLearning(ForgetFanAdaptiveLearningRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received ForgetFanAdaptiveLearning for fan {FanIndex}.", request.FanIndex);
            _authorizationService.EnsureCommandAccess();

            if (_previewWatchdog.HasOpenHold(request.FanIndex))
            {
                throw new InvalidOperationException(
                    $"Fan {request.FanIndex} has a live preview open. Apply or discard it before discarding its learned model.");
            }

            if (!_fanControlStateStore.ForgetAdaptiveLearning(request.FanIndex))
            {
                return new FanCurveProfileOperationReply
                {
                    FanIndex = request.FanIndex,
                    Succeeded = false,
                    Message = $"Unknown fan {request.FanIndex}.",
                };
            }

            // Clearing the stored model is only half of it — the running controller holds the fit in memory.
            _fanControlWorkerSignals.RequestControllerReset(request.FanIndex);

            await PersistFanControlStateAsync(request.FanIndex, preview: false, context.CancellationToken).ConfigureAwait(false);

            return new FanCurveProfileOperationReply { FanIndex = request.FanIndex, Succeeded = true, Message = string.Empty };
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected ForgetFanAdaptiveLearning for fan {FanIndex} because the service was not in a writable state.", request.FanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    private static TemperatureAggregationMode ParseAggregation(TemperatureAggregationModeValue value)
        => TelemetryGrpcMapper.TryParseTemperatureAggregationMode(value, out var mode)
            ? mode
            : TemperatureAggregationMode.Maximum;

    private static AdaptiveFanSettings ToAdaptiveSettings(AdaptiveFanSettingsMessage message)
        => new AdaptiveFanSettings
        {
            TargetTemperatureCelsius = message.TargetTemperatureCelsius,
            SafetyFloorEnabled = message.SafetyFloorEnabled,
            SafetyFloorPercent = message.SafetyFloorPercent,
            // 0 is an older client's unset proto default, not a chosen pace — the valid range starts above it.
            LambdaSeconds = message.LambdaSeconds > 0d ? message.LambdaSeconds : AdaptivePidTuning.DefaultLambdaSeconds,
        }.Sanitized();

    public override async Task HoldFanPreview(HoldFanPreviewRequest request, IServerStreamWriter<HoldFanPreviewReply> responseStream, ServerCallContext context)
    {
        var fanIndex = request.FanIndex;

        // This was the ONE mutating path on this service with no authorization check, and it is genuinely
        // mutating: the finally block below reverts the fan by calling SetFanDutyAsync / RestoreAutoFanControl
        // directly, and FrameworkDataProvider.EnsureWritableConnection only checks that EC polling is live —
        // it never consults this service. So opening a hold and disconnecting wrote the persisted Manual duty
        // (or 100% for Max) to the EC with AllowFanControlCommands still false, which is precisely the
        // fail-closed guarantee SECURITY.md makes. Checked up front so no hold is ever opened — and therefore
        // no revert is ever owed — while commands are disabled.
        _authorizationService.EnsureCommandAccess();
        EnsureNotCalibrating(fanIndex);

        _logger.LogInformation("Opening preview hold for fan {FanIndex}.", fanIndex);

        // Capture the fan's current (applied) state before any preview command mutates it. The client waits
        // for the ready reply below before sending its preview command, so this captures the pre-preview state.
        // The token identifies THIS stream's hold; it is null when another stream already holds the fan.
        Guid? holdToken = null;
        if (_fanControlStateStore.GetState(fanIndex) is { } prePreview)
        {
            holdToken = _previewWatchdog.Begin(fanIndex, prePreview);
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
            if (holdToken is null)
            {
                // This stream never owned a hold — either the fan had no state to capture, or another stream
                // got there first. Reverting OR releasing here would act on someone else's hold and pull the
                // fan out from under a client that is still previewing it.
                _logger.LogInformation("Closed preview hold stream for fan {FanIndex} that held no hold of its own.", fanIndex);
            }

            // Revert only if the hold is still active (not committed/released by an Apply or client-side
            // restore) and the service is not shutting down (the shutdown coordinator restores fans to Auto).
            else if (!_applicationLifetime.ApplicationStopping.IsCancellationRequested
                && _previewWatchdog.TryTakeForRevert(fanIndex, holdToken, out var snapshot))
            {
                _logger.LogWarning("Preview hold for fan {FanIndex} closed without commit; reverting to the pre-preview state.", fanIndex);
                await RestoreFanStateAsync(fanIndex, snapshot, "its preview hold dropped", CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _previewWatchdog.Release(fanIndex);
                _logger.LogInformation("Closed preview hold for fan {FanIndex} (committed or shutting down).", fanIndex);
            }
        }
    }

    /// <summary>
    /// Runs a calibration, streaming progress, and puts the fan back however it ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's stream is the run's lease. Cancelling the call — or the client simply dying — cancels
    /// <see cref="FanCalibrationRunner.RunAsync"/>, whose own finally stops the CPU load and hands the fan to
    /// firmware control. That is the safety floor and it holds without anything here running.
    /// </para>
    /// <para>
    /// This method adds the courtesy on top of that floor: returning the fan to the mode the user actually had
    /// before the run, rather than leaving it on Auto.
    /// </para>
    /// </remarks>
    public override async Task RunFanCalibration(
        RunFanCalibrationRequest request,
        IServerStreamWriter<FanCalibrationProgressReply> responseStream,
        ServerCallContext context)
    {
        var fanIndex = request.FanIndex;

        // Checked up front, like HoldFanPreview: a calibration writes duty to the EC directly, so it may not
        // start while fan-control commands are disabled. Mapped rather than allowed to escape, so a refusal
        // arrives as FailedPrecondition instead of an opaque Unknown.
        try
        {
            _authorizationService.EnsureCommandAccess();
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected a calibration for fan {FanIndex} because the service was not in a writable state.", fanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }

        if (_previewWatchdog.HasOpenHold(fanIndex))
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Fan {fanIndex} has a live preview open. Apply or discard it before calibrating."));
        }

        // Captured before the first duty command, so it describes what the user had rather than anything the
        // run did.
        var captured = _fanControlStateStore.GetState(fanIndex);

        _logger.LogInformation("Starting calibration for fan {FanIndex} at a client's request.", fanIndex);

        // Writing to a client that has gone throws InvalidOperationException("...the request is complete"),
        // which the arbiter's catch below would then misreport as "another calibration is running" — a
        // FailedPrecondition raised at the one moment there is nobody left to receive it. A cancelled run
        // still RETURNS a result rather than throwing, so the final write is the likeliest to land on a dead
        // stream, and the guard has to cover both writes rather than only that one.
        async Task WriteIfLiveAsync(FanCalibrationProgressReply reply)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await responseStream.WriteAsync(reply).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The client went away between the check and the write. Nothing to report it to.
            }
        }

        try
        {
            var result = await _calibrationRunner.RunAsync(
                fanIndex,
                [.. request.DrivingSensorIndices],
                progress => WriteIfLiveAsync(MapCalibrationProgress(progress)),
                context.CancellationToken,
                // Unset means "you decide", which is what the service did before the choice was askable.
                request.HasLoadTarget ? (ThermalLoadTarget)request.LoadTarget : ThermalLoadTarget.None)
                .ConfigureAwait(false);

            await WriteIfLiveAsync(MapCalibrationResult(result)).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            // Another calibration already owns a fan. Not an error in this request so much as a fact about
            // the machine, so it comes back as a precondition rather than an internal fault.
            _logger.LogWarning(exception, "Rejected a calibration for fan {FanIndex}.", fanIndex);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
        catch (OperationCanceledException)
        {
            // The client cancelled or disconnected. The runner has already stopped the load and restored the
            // fan; there is no one left to tell.
            _logger.LogInformation("The calibration stream for fan {FanIndex} closed before the run finished.", fanIndex);
        }
        finally
        {
            await RestoreAfterCalibrationAsync(fanIndex, captured).ConfigureAwait(false);

            // The model the run just measured lived ONLY in memory: nothing on the calibration path wrote it
            // to the configuration file. Because that file is watched with reloadOnChange, the next command
            // on any other fan rewrote it and re-overlaid this fan with Calibration = None — silently
            // discarding a multi-minute hot test and re-locking Adaptive behind "needs to learn this fan
            // first". CancellationToken.None on purpose: a run the client abandoned must still keep what it
            // learned, and this is a local file write with nothing left to cancel it for.
            await PersistFanControlStateAsync(fanIndex, preview: false, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns a fan to the mode it had before a calibration, without discarding what the run just learned.
    /// </summary>
    /// <remarks>
    /// The captured snapshot carries the OLD calibration, so restoring it wholesale would overwrite the model
    /// the run just produced with the one it replaced — quietly throwing away the entire point of the run. The
    /// split below is deliberate: what the USER configured comes from the capture, what the MACHINE learned
    /// comes from the live state.
    /// </remarks>
    private async Task RestoreAfterCalibrationAsync(int fanIndex, FanControlStateSnapshot? captured)
    {
        if (captured is null)
        {
            return;
        }

        // The shutdown coordinator restores every fan to Auto on the way out; re-applying a mode here would
        // race it and could leave the EC driven after the service has gone.
        if (_applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogInformation("Skipping the post-calibration restore for fan {FanIndex} because the service is stopping.", fanIndex);
            return;
        }

        var current = _fanControlStateStore.GetState(fanIndex);
        var restoreTo = current is null
            ? captured
            : captured with
            {
                Calibration = current.Calibration,
                AdaptiveLearning = current.AdaptiveLearning,
            };

        await RestoreFanStateAsync(fanIndex, restoreTo, "the calibration finished", CancellationToken.None).ConfigureAwait(false);
    }

    // Optional scalars are assigned only when present. proto3 gives them a Has flag rather than a nullable
    // property, so writing a default would tell the client "0 C" where the truth is "no reading" — and on a
    // live plot those two look nothing alike.
    private static FanCalibrationProgressReply MapCalibrationProgress(FanCalibrationProgress progress)
    {
        var reply = new FanCalibrationProgressReply
        {
            FanIndex = progress.FanIndex,
            Step = MapCalibrationStep(progress.Step),
            StepCount = progress.StepCount,
            ElapsedSeconds = progress.ElapsedSeconds,
            IsStepMarker = progress.IsStepMarker,
            IsComplete = false,
            OverallProgress = progress.OverallProgress,
            PowerIsSystemWide = progress.PowerIsSystemWide,
        };

        if (progress.TemperatureCelsius is double celsius)
        {
            reply.TemperatureCelsius = celsius;
        }

        if (progress.DutyPercent is double duty)
        {
            reply.DutyPercent = duty;
        }

        if (progress.SpeedRpm is double rpm)
        {
            reply.SpeedRpm = rpm;
        }

        if (progress.PackagePowerWatts is double watts)
        {
            reply.PackagePowerWatts = watts;
        }

        if (progress.EstimatedRemaining is TimeSpan remaining)
        {
            reply.EstimatedRemainingMilliseconds = (long)remaining.TotalMilliseconds;
        }

        if (progress.ClockMegahertz is double clockMegahertz)
        {
            reply.ClockMegahertz = clockMegahertz;
        }

        if (progress.UtilizationPercent is double utilizationPercent)
        {
            reply.UtilizationPercent = utilizationPercent;
        }

        return reply;
    }

    private static FanCalibrationProgressReply MapCalibrationResult(FanCalibrationRunResult result)
    {
        var reply = new FanCalibrationProgressReply
        {
            FanIndex = result.FanIndex,
            Step = MapCalibrationStep(result.StoppedAt),
            StepCount = (int)FanCalibrationStep.Completed - 1,
            IsComplete = true,
            Succeeded = result.Succeeded,
            Failure = MapCalibrationFailure(result.Failure),
            DurationMilliseconds = (long)result.Duration.TotalMilliseconds,
            FansRestored = result.FansRestored,
            ElapsedSeconds = result.Duration.TotalSeconds,
        };

        // Carried on failures too. "The machine never got busy enough" without saying how busy it did get
        // leaves the user with nothing to change before spending another several minutes on it.
        if (result.AveragePackagePowerWatts is double averageWatts)
        {
            reply.AveragePackagePowerWatts = averageWatts;
        }

        if (result.TemperatureSwingCelsius is double swing)
        {
            reply.TemperatureSwingCelsius = swing;
        }

        if (result.PeakTemperatureCelsius is double peak)
        {
            reply.PeakTemperatureCelsius = peak;
        }

        if (result.Calibration is { } calibration)
        {
            reply.Calibration = TelemetryGrpcMapper.MapCalibration(calibration);
        }

        return reply;
    }

    private static FanCalibrationStepValue MapCalibrationStep(FanCalibrationStep step) => step switch
    {
        FanCalibrationStep.SettlingAtIdle => FanCalibrationStepValue.SettlingAtIdle,
        FanCalibrationStep.FindingMinimumSpin => FanCalibrationStepValue.FindingMinimumSpin,
        FanCalibrationStep.LoadingAndSettling => FanCalibrationStepValue.LoadingAndSettling,
        FanCalibrationStep.SteppingFan => FanCalibrationStepValue.SteppingFan,
        FanCalibrationStep.MeasuringResponse => FanCalibrationStepValue.MeasuringResponse,
        FanCalibrationStep.FittingModel => FanCalibrationStepValue.FittingModel,
        FanCalibrationStep.VerifyingSpeedTracking => FanCalibrationStepValue.VerifyingSpeedTracking,
        FanCalibrationStep.MeasuringGainCurve => FanCalibrationStepValue.MeasuringGainCurve,
        FanCalibrationStep.CoolingDown => FanCalibrationStepValue.CoolingDown,
        FanCalibrationStep.Completed => FanCalibrationStepValue.Completed,
        _ => FanCalibrationStepValue.None,
    };

    // ClientDisconnected has no case here on purpose: the runner sees a cancelled token and cannot tell a user
    // pressing Cancel from a client that died, so reporting either as the other would be a guess. Both arrive
    // as Cancelled, which is true of both.
    private static FanCalibrationFailureValue MapCalibrationFailure(FanCalibrationFailure failure) => failure switch
    {
        FanCalibrationFailure.InsufficientLoad => FanCalibrationFailureValue.InsufficientLoad,
        FanCalibrationFailure.InsufficientTemperatureSwing => FanCalibrationFailureValue.InsufficientTemperatureSwing,
        FanCalibrationFailure.TemperatureCeiling => FanCalibrationFailureValue.TemperatureCeiling,
        FanCalibrationFailure.Cancelled => FanCalibrationFailureValue.Cancelled,
        FanCalibrationFailure.ClientDisconnected => FanCalibrationFailureValue.ClientDisconnected,
        FanCalibrationFailure.InsufficientData => FanCalibrationFailureValue.InsufficientData,
        FanCalibrationFailure.OnBattery => FanCalibrationFailureValue.OnBattery,
        FanCalibrationFailure.GpuLoadUnavailable => FanCalibrationFailureValue.GpuLoadUnavailable,
        _ => FanCalibrationFailureValue.None,
    };

    // Restores a fan to a captured state after something that took it over ends. Mirrors the commit actuation
    // paths; a curve or adaptive snapshot is re-published so the curve worker re-actuates the EC.
    private async Task RestoreFanStateAsync(int fanIndex, FanControlStateSnapshot prePreview, string reason, CancellationToken cancellationToken)
    {
        // Matches the client/service default manual duty when a Manual pre-state never recorded one.
        const double defaultManualDutyPercent = 50d;

        // Re-checked here as well as at HoldFanPreview's entry, because AllowFanControlCommands is settable
        // over the same socket WHILE a hold is open: a hold opened legitimately can outlive the permission it
        // was opened under. If the service may no longer write duty, it may not write it on the way out
        // either — the fan simply stays where it is, which is the safe reading of "commands are disabled".
        // Restoring firmware control is deliberately still allowed below: handing the EC back is the one
        // action that is always safe and is what disabling fan control should converge on.
        if (!_authorizationService.IsFanControlEnabled && prePreview.Mode is FanControlMode.Manual or FanControlMode.Max)
        {
            _logger.LogWarning(
                "Fan control was disabled while fan {FanIndex} had an open preview; returning it to firmware control instead of re-applying {Mode}.",
                fanIndex,
                prePreview.Mode);

            try
            {
                await _frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, cancellationToken).ConfigureAwait(false);
                _fanControlStateStore.MarkAuto(fanIndex);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to return fan {FanIndex} to firmware control after fan control was disabled.", fanIndex);
            }

            return;
        }

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
                // Both are republish-and-let-the-worker-actuate: the mode is a rule the worker evaluates, not
                // a duty to write. Adaptive used to fall into the default below and land on Auto, silently
                // disarming a closed loop the user had turned on.
                case FanControlMode.CustomCurve:
                case FanControlMode.Adaptive:
                    _fanControlStateStore.RestoreState(prePreview);
                    break;
                default:
                    await _frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, cancellationToken).ConfigureAwait(false);
                    _fanControlStateStore.MarkAuto(fanIndex);
                    break;
            }

            _logger.LogInformation("Restored fan {FanIndex} to {Mode} after {Reason}.", fanIndex, prePreview.Mode, reason);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to restore fan {FanIndex} after {Reason}.", fanIndex, reason);
        }
    }

    private static void EnsureSlotInRange(int slot)
    {
        if (slot is < 0 or >= FrameworkFanControlStateStore.MaxCurveProfileSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"Curve profile slot must be between 0 and {FrameworkFanControlStateStore.MaxCurveProfileSlots - 1}.");
        }
    }

    public override async Task<ResetFanControlToFactoryDefaultsReply> ResetFanControlToFactoryDefaults(ResetFanControlToFactoryDefaultsRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Received ResetFanControlToFactoryDefaults.");
            _authorizationService.EnsureCommandAccess();

            // A wipe during a measurement would pull the run's pinned fans out from under it.
            EnsureNotCalibrating(fanIndex: 0);

            // A hold closing after the reset would revert its fan to the captured pre-preview state,
            // resurrecting exactly what is being wiped. Drop every hold first — the reset returns the fan to
            // EC automatic control, which is a strictly safer end state than any revert target.
            var releasedHolds = _previewWatchdog.ReleaseAll();
            if (releasedHolds.Length > 0)
            {
                _logger.LogInformation("Released {HoldCount} open preview hold(s) before the factory reset.", releasedHolds.Length);
            }

            // In-memory state FIRST, unlike the per-fan RestoreAutoFanControl below: the curve worker
            // re-asserts curve / manual / max duty from its mirror of this store on every evaluation tick, so
            // restoring the EC before the store flips to Auto lets an in-flight tick immediately re-drive the
            // fan. A single per-fan command races nothing meaningful; a whole-store wipe does.
            var fanIndices = _fanControlStateStore.ResetAllToFactoryDefaults();

            var restoredCount = 0;
            var failedCount = 0;
            foreach (var fanIndex in fanIndices)
            {
                try
                {
                    await _frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, context.CancellationToken).ConfigureAwait(false);
                    restoredCount++;
                }
                catch (Exception exception)
                {
                    failedCount++;
                    _logger.LogWarning(exception, "Failed to restore automatic fan control for fan {FanIndex} during the factory reset. Its stored settings were still cleared.", fanIndex);
                }
            }

            // One write for the whole array — this also clears orphan entries for fan indices the hardware no
            // longer reports, which the in-memory pass above cannot see.
            var clearedEntries = 0;
            string? persistenceFailure = null;
            try
            {
                clearedEntries = await _configurationStore.ClearAllFanControlStatesAsync(context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                persistenceFailure = exception.Message;
                _logger.LogWarning(exception, "Reset fan control state in memory but failed to clear the persisted copy. The old settings would come back after a service restart.");
            }

            _logger.LogInformation(
                "Completed ResetFanControlToFactoryDefaults. FansRestored={FansRestored}, FansFailed={FansFailed}, PersistedEntriesCleared={PersistedEntriesCleared}.",
                restoredCount,
                failedCount,
                clearedEntries);

            return new ResetFanControlToFactoryDefaultsReply
            {
                Succeeded = failedCount == 0 && persistenceFailure is null,
                Message = BuildResetMessage(restoredCount, failedCount, clearedEntries, persistenceFailure),
                FansRestored = restoredCount,
                FansFailed = failedCount,
                PersistedEntriesCleared = clearedEntries,
            };
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected ResetFanControlToFactoryDefaults because the service was not in a writable state.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    // A partial reset is real information the user needs, so EC and persistence failures are reported through
    // the reply rather than thrown: the fans that did reset stay reset either way.
    private static string BuildResetMessage(int restoredCount, int failedCount, int clearedEntries, string? persistenceFailure)
    {
        if (persistenceFailure is not null)
        {
            return $"Fans were returned to automatic control, but the saved fan settings could not be deleted: {persistenceFailure}";
        }

        if (failedCount > 0)
        {
            return $"Cleared {clearedEntries} saved fan setting(s) and returned {restoredCount} fan(s) to automatic control, but {failedCount} fan(s) could not be restored on the controller.";
        }

        return $"Returned {restoredCount} fan(s) to automatic control and cleared {clearedEntries} saved fan setting(s).";
    }

    private static FanCurveProfileOperationReply SucceededProfileReply(int fanIndex, int slot)
        => new() { FanIndex = fanIndex, Slot = slot, Succeeded = true, Message = string.Empty };

    /// <summary>
    /// The warning a reply carries when the command took effect live but could not be written to disk. The
    /// command still succeeds — the fan IS doing what was asked — but claiming plain success taught users
    /// their applied Adaptive "randomly" reverted on the next restart, with the truth visible only in the
    /// Event Log.
    /// </summary>
    internal const string PersistenceFailedWarning =
        "Applied, but saving to disk failed — this will not survive a service restart. "
        + "Check write permissions on the service's configuration folder.";

    /// <summary>Returns false when the state took effect in memory but could not be written to disk.</summary>
    /// <summary>
    /// Refuses a command that would drive a fan while a calibration owns the machine.
    /// </summary>
    /// <remarks>
    /// The arbiter's claim was enforced only against the curve worker, so a SECOND client could still write
    /// duty, switch a mode, or reset to defaults in the middle of a measurement — unpinning the controlled
    /// conditions the fit assumes and silently corrupting the identified model, which the run would then
    /// store as if nothing had happened. The claim covers EVERY fan for the life of a run (a run pins the
    /// fans it is not measuring), which is exactly the scope this needs.
    /// </remarks>
    private void EnsureNotCalibrating(int fanIndex)
    {
        if (_calibrationArbiter.IsCalibrating(fanIndex))
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "A fan calibration is running. Wait for it to finish, or stop it, before changing fan control."));
        }
    }

    private async Task<bool> PersistFanControlStateAsync(int fanIndex, bool preview, CancellationToken cancellationToken)
    {
        // A preview is volatile: the EC and the in-memory store reflect it (so live clients see it), but it
        // is never written to the configuration store. A service restart therefore restores the last applied
        // state, and "Apply" simply re-sends the same command with preview=false to persist it.
        if (preview)
        {
            return true;
        }

        // A persisting command commits (or restores) the fan, so any open preview hold must not later revert it.
        _previewWatchdog.Release(fanIndex);

        var options = _fanControlStateStore.BuildFanControlOptions(fanIndex);
        if (options is null)
        {
            return true;
        }

        try
        {
            await _configurationStore.UpsertFanControlStateAsync(options, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception persistenceException)
        {
            _logger.LogWarning(persistenceException, "Saved fan curve profiles for fan {FanIndex} in memory but failed to persist them. They will not survive a service restart.", fanIndex);
            return false;
        }
    }
}
