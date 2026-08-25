using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// The adaptive fan controller for ONE fan: holds a driving temperature at a target using as little airflow
/// as it can, by combining a feed-forward estimate from CPU power with PI feedback on temperature error.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this shape.</b> A fan curve maps temperature to duty, which means it can only ever react to heat
/// that has already arrived at a sensor several seconds downstream of the die. Feed-forward reads CPU package
/// power — which moves the instant a workload starts — and spins the fan for heat that is still in transit.
/// PI then corrects whatever the feed-forward estimate got wrong. That split is the whole design: the fast,
/// approximate term handles the transient, and the slow, exact term handles the steady state.
/// </para>
/// <para>
/// <b>Cascade.</b> Where calibration verified the EC tracks commanded speeds, the duty demand is converted to
/// an RPM setpoint and the firmware's own loop holds it. That inner loop runs far faster than this one and
/// absorbs the fan's non-linear duty-to-RPM curve, so equal duty demands produce equal airflow regardless of
/// where on the curve the fan is sitting.
/// </para>
/// <para>
/// <b>Statefulness.</b> This type holds integrator and latch state, so one instance belongs to one fan and
/// must be driven from one thread (the curve worker's serialized evaluation). It is not thread-safe and does
/// not try to be — making it so would hide the fact that two callers stepping the same integrator is a bug.
/// </para>
/// </remarks>
public sealed class AdaptiveFanController
{
    /// <summary>
    /// How long the driving temperature must stay below target before a throttle latch releases.
    /// </summary>
    /// <remarks>
    /// A latch that released the moment temperature dipped under target would drop the fan straight back into
    /// the conditions that caused the throttle, and oscillate. One minute of sustained margin is the design's
    /// figure and is long enough to mean the workload actually eased.
    /// </remarks>
    public static readonly TimeSpan ThrottleLatchReleaseWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Duty added while a throttle latch is held.
    /// </summary>
    /// <remarks>
    /// A throttle event means the cooling system already lost — the processor gave up clock speed to protect
    /// itself. The response is deliberately a blunt fixed step rather than something proportional: at that
    /// point the model has been proven wrong for these conditions, so leaning harder on the model is not the
    /// answer.
    /// </remarks>
    public const double ThrottleEscalationDutyPercent = 15d;

    /// <summary>
    /// Performance ratio below which the processor counts as throttling.
    /// </summary>
    /// <remarks>
    /// <see cref="ControlTelemetrySample.CpuPerformanceRatio"/> is current clock over base clock, so values
    /// above 1 are turbo and normal. Sustained values well below 1 mean the chip is not reaching its rated
    /// speed. 0.85 leaves room for ordinary idle downclocking, which is not thermal throttling and must not
    /// latch the fans up.
    /// </remarks>
    public const double ThrottlePerformanceRatioThreshold = 0.85d;

    /// <summary>
    /// Consecutive throttling samples required before latching.
    /// </summary>
    /// <remarks>
    /// A single low sample is far more likely to be a scheduler artefact or a power-state transition than a
    /// thermal event. Requiring persistence keeps a latch — which pins the fan up for at least a minute —
    /// from firing on noise.
    /// </remarks>
    public const int ThrottleLatchSampleThreshold = 3;

    /// <summary>
    /// Maximum temperature slope, in °C/s, that the lead term will act on.
    /// </summary>
    /// <remarks>
    /// Sensor noise differentiates into large spurious slopes. Capping the term keeps a one-sample spike from
    /// slamming the fan; a genuine thermal runaway is handled by PI and the throttle latch, not by lead.
    /// </remarks>
    public const double MaximumLeadSlopeCelsiusPerSecond = 2d;

    /// <summary>Duty points contributed per °C/s of rise.</summary>
    /// <remarks>
    /// Scaled so the capped slope contributes at most <c>MaximumLeadSlope × this</c> = 20 duty points — enough
    /// to matter on a fast ramp, not enough to dominate the terms that are actually closing the loop.
    /// </remarks>
    public const double LeadDutyPerCelsiusPerSecond = 10d;

    /// <summary>
    /// The slope filter's half-life. Long relative to a tick, short relative to the plant time constant.
    /// </summary>
    public static readonly TimeSpan LeadSlopeHalfLife = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How fast the commanded duty may RISE, in duty points per second.
    /// </summary>
    /// <remarks>
    /// Fast, but not a step. The fan must be free to answer a real transient immediately; slamming from idle
    /// to full in one tick is audible as a bang rather than a ramp, and buys nothing thermally because the
    /// heatsink cannot absorb it any faster.
    /// </remarks>
    public const double MaximumRiseDutyPointsPerSecond = 60d;

    /// <summary>
    /// How fast the commanded duty may FALL, in duty points per second.
    /// </summary>
    /// <remarks>
    /// Deliberately an order of magnitude slower than the rise, and purely a perceived-noise decision. A
    /// symmetric rate limit makes a PI-controlled fan surge: every correction is as audible on the way down
    /// as on the way up, and the ear notices a fan dropping far more than one holding. A full sweep down
    /// takes about 17 seconds, which reads as the machine relaxing rather than as the controller hunting.
    /// This guard is what makes the difference between "adaptive" and "annoying".
    /// </remarks>
    public const double MaximumFallDutyPointsPerSecond = 6d;

    private readonly SignalSmoothingFilter _powerFilter;
    private readonly SignalSmoothingFilter _slopeFilter;
    private readonly AdaptiveModelLearner _learner;
    private readonly ThermalLoadPolicy _loadPolicy;

    private double _integratorDutyPercent;
    private double? _lastCommandedDutyPercent;
    private ThermalLoadSource _lastThermalLoadSource;
    private bool _isThermalLoadSettled;
    private double? _previousTemperatureCelsius;
    private double _lastTemperatureSlope;
    private int _consecutiveThrottleSamples;
    private bool _isThrottleLatched;
    private DateTimeOffset? _throttleLatchedAt;
    private TimeSpan _belowTargetDuration;

    /// <summary>Creates a controller, optionally resuming what this fan has already learned.</summary>
    /// <param name="learningState">Previously learned state to resume, or null to start from the calibration.</param>
    public AdaptiveFanController(AdaptiveLearningState? learningState = null)
    {
        _learner = new AdaptiveModelLearner(learningState);

        // Resume the composition a previous run settled on. Re-running the capability window every restart
        // could land somewhere different and silently invalidate the fit that was persisted alongside it.
        _loadPolicy = new ThermalLoadPolicy(learningState?.ThermalLoadSource ?? ThermalLoadSource.None);

        // Power leads temperature, so it is taken instantly on the way up and decayed on the way down: a
        // workload starting must reach feed-forward at once, while a momentary dip must not drop the fan.
        _powerFilter = new SignalSmoothingFilter(TimeSpan.FromSeconds(5));

        // The slope filter smooths in BOTH directions — an unsmoothed rate of change is mostly sensor noise.
        _slopeFilter = new SignalSmoothingFilter(LeadSlopeHalfLife, fastAttack: false);
    }

    /// <summary>True while a throttle escalation is held.</summary>
    public bool IsThrottleLatched => _isThrottleLatched;

    /// <summary>
    /// What continuous operation has taught this fan since calibration. Persist it so the machine keeps
    /// improving across restarts rather than relearning every boot.
    /// </summary>
    public AdaptiveLearningState LearningState => _learner.State;

    /// <summary>
    /// Steps the controller one tick and returns the duty to command, decomposed into its terms.
    /// </summary>
    /// <param name="calibration">The fan's learned model. An unusable model yields <see cref="AdaptiveControlDecision.NotDriven"/>.</param>
    /// <param name="settings">The user's target and safety floor.</param>
    /// <param name="drivingTemperatureCelsius">The current driving temperature, in canonical °C.</param>
    /// <param name="controlTelemetry">The primary-tier CPU signals; may carry nothing.</param>
    /// <param name="elapsed">Time since the previous tick. Zero or negative disables the time-dependent terms for this tick.</param>
    /// <param name="timestamp">Now, used to stamp a throttle latch.</param>
    /// <returns>The decomposed decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="calibration"/> or <paramref name="settings"/> is null.</exception>
    public AdaptiveControlDecision Evaluate(
        FanCalibrationSnapshot calibration,
        AdaptiveFanSettings settings,
        double drivingTemperatureCelsius,
        ControlTelemetrySample controlTelemetry,
        TimeSpan elapsed,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(settings);

        if (!double.IsFinite(drivingTemperatureCelsius))
        {
            Reset();
            return AdaptiveControlDecision.NotDriven;
        }

        // The calibration is the single source of the anchor, and it arrives here rather than at construction
        // so a recalibration mid-session supersedes what was identified around the model it replaced.
        _learner.Anchor(calibration.FeedForwardDutyPerWatt);

        // What the loop runs on: the calibration (or the conservative bootstrap when there is none), with the
        // parameters live operation can resolve overridden by what it identified. The usability check below is
        // against the MERGED model, not the raw calibration — "never calibrated" is no longer a reason to
        // leave a fan to firmware.
        var model = _learner.EffectiveModel(calibration);

        if (!model.IsUsable)
        {
            Reset();
            return AdaptiveControlDecision.NotDriven;
        }

        var sanitized = settings.Sanitized();
        var target = sanitized.TargetTemperatureCelsius;

        // Positive error means too hot, which must raise duty. Every term below follows that sign.
        var error = drivingTemperatureCelsius - target;

        // Gains are DERIVED here, not read from the calibration: λ is a user setting, so a slider move must
        // retune the loop without re-measuring the machine.
        //
        // GAIN SCHEDULING: the process gain is taken at the duty the fan is actually at, not as one average
        // for the whole range. Fan cooling is strongly nonlinear — a duty point buys several times more at
        // 20% than at 90% — and since the rule divides by that gain, an averaged value makes the loop far
        // more aggressive than designed at low duty, which is exactly where the fan is quiet enough to be
        // heard hunting. Falls back to the averaged gain when no curve was measured.
        var scheduled = model with
        {
            ProcessGainCelsiusPerPercent = model.GainCurve.GainAt(
                _lastCommandedDutyPercent ?? model.MinimumSpinDutyPercent,
                model.ProcessGainCelsiusPerPercent),
        };

        var gains = AdaptivePidTuning.Compute(scheduled, sanitized.LambdaSeconds);

        var feedForward = ComputeFeedForward(controlTelemetry, elapsed, out var feedForwardUnavailable);
        var proportionalIntegral = ComputeProportionalIntegral(gains, error, elapsed);
        var lead = ComputeLead(drivingTemperatureCelsius, elapsed);
        var escalation = UpdateThrottleLatch(controlTelemetry, error, elapsed, timestamp);

        var raw = feedForward + proportionalIntegral + lead + escalation;
        var limited = ApplyLimits(raw, model, sanitized);

        // Rate-limit LAST, so the floor and the stall guard are never violated by a ramp that has not caught
        // up yet, and so anti-windup below still sees the clamp that actually bound.
        limited = ApplySlewLimit(limited, elapsed);

        // Anti-windup by BACK-CALCULATION: bleed the saturated excess out of the integrator over a tracking
        // time constant, rather than all at once. Applying the whole gap in a single tick looks like a
        // stronger correction but is badly wrong — the gap includes the proportional term, so on a machine
        // sitting far below target it slams the integrator from one rail to the other, and the loop then
        // fights its own correction on the next disturbance.
        UnwindIntegratorIfSaturated(raw, limited, gains, elapsed);

        // Learn AFTER limiting, so the learner sees whether the demand was actually delivered. Feeding it the
        // raw demand would teach it from duty the fan never produced.
        var isSaturated = Math.Abs(raw - limited) > 1e-9d;
        _learner.Observe(
            new AdaptiveLearningObservation
            {
                PackagePowerWatts = _powerFilter.Current ?? 0d,

                // An unsettled composition is reported as None, which the learner refuses. Feed-forward above
                // already used the value; only learning has to wait for the meaning to be fixed.
                ThermalLoadSource = _isThermalLoadSettled ? _lastThermalLoadSource : ThermalLoadSource.None,
                TemperatureCelsius = drivingTemperatureCelsius,
                TemperatureErrorCelsius = error,
                CommandedDutyPercent = limited,
                TemperatureSlopeCelsiusPerSecond = _lastTemperatureSlope,
                FeedForwardDutyPercent = feedForward,
                ProportionalIntegralDutyPercent = proportionalIntegral,
                IsSaturated = isSaturated,
                IsThrottleLatched = _isThrottleLatched,
            },
            elapsed,
            timestamp);

        var setpointRpm = model.TrackingMode == FanSpeedTrackingMode.Cascade
            ? ToSetpointRpm(limited, model)
            : null;

        return new AdaptiveControlDecision
        {
            IsDriven = true,
            FeedForwardDutyPercent = feedForward,
            ProportionalIntegralDutyPercent = proportionalIntegral,
            LeadDutyPercent = lead,
            ThrottleEscalationDutyPercent = escalation,
            RawDutyPercent = raw,
            DutyPercent = limited,
            SetpointRpm = setpointRpm,
            DrivingTemperatureCelsius = drivingTemperatureCelsius,
            TargetTemperatureCelsius = target,
            IsThrottleLatched = _isThrottleLatched,
            ThrottleLatchedAt = _throttleLatchedAt,
            ThrottleLatchReleaseSeconds = _isThrottleLatched
                ? Math.Max(0d, (ThrottleLatchReleaseWindow - _belowTargetDuration).TotalSeconds)
                : null,
            IsFeedForwardUnavailable = feedForwardUnavailable,
            Learning = _learner.State,
        };
    }

    /// <summary>
    /// Clears the throttle latch immediately, for the UI's "Release now".
    /// </summary>
    /// <remarks>
    /// The user overriding a safety escalation is legitimate — they can see the machine and we cannot — but
    /// it only clears the LATCH. If the processor is still throttling, the next tick latches again, which is
    /// the correct outcome rather than something to suppress.
    /// </remarks>
    public void ReleaseThrottleLatch()
    {
        _isThrottleLatched = false;
        _throttleLatchedAt = null;
        _belowTargetDuration = TimeSpan.Zero;
        _consecutiveThrottleSamples = 0;
    }

    /// <summary>
    /// Drops all accumulated state.
    /// </summary>
    /// <remarks>
    /// Called when the fan stops being adaptively driven. Integrator state from before a mode switch
    /// describes a machine in a different configuration; carrying it across would make the first seconds
    /// after re-entering Adaptive react to history the user already discarded.
    /// </remarks>
    public void Reset()
    {
        _integratorDutyPercent = 0d;
        _previousTemperatureCelsius = null;
        _lastTemperatureSlope = 0d;
        _lastCommandedDutyPercent = null;
        _powerFilter.Reset();
        _slopeFilter.Reset();

        // The dwell timers go, the learned GAIN stays — it describes the chassis, which did not change just
        // because this fan left Adaptive for a while.
        _learner.ResetDwell();
        ReleaseThrottleLatch();
    }

    private double ComputeFeedForward(
        ControlTelemetrySample controlTelemetry,
        TimeSpan elapsed,
        out bool unavailable)
    {
        var (watts, source, isSettled) = controlTelemetry is null
            ? (null, ThermalLoadSource.None, false)
            : _loadPolicy.Resolve(controlTelemetry);

        _lastThermalLoadSource = source;

        // Until the policy settles the composition can still change, so nothing learned from these samples
        // would be comparable. Feed-forward still runs on them — it does not care about comparability.
        _isThermalLoadSettled = isSettled;

        if (watts is not double packageWatts || !double.IsFinite(packageWatts) || packageWatts < 0d)
        {
            // No power reading. Decay whatever feed-forward was in flight rather than dropping it to zero in
            // one tick: a reader that blinks out for a sample must not produce an audible fan dip, and PI
            // will take over the load within a few seconds either way.
            unavailable = true;
            var decayed = _powerFilter.Sample(null, elapsed) ?? 0d;
            return Math.Max(0d, decayed * _learner.EffectiveFeedForwardDutyPerWatt);
        }

        unavailable = false;
        var smoothedWatts = _powerFilter.Sample(packageWatts, elapsed) ?? packageWatts;
        var feedForward = smoothedWatts * _learner.EffectiveFeedForwardDutyPerWatt;

        return double.IsFinite(feedForward) ? Math.Clamp(feedForward, 0d, 100d) : 0d;
    }

    private double ComputeProportionalIntegral(AdaptivePidGains gains, double error, TimeSpan elapsed)
    {
        if (!gains.IsUsable)
        {
            return 0d;
        }

        var proportional = gains.ProportionalGain * error;

        if (elapsed > TimeSpan.Zero && gains.IntegralGain > 0d)
        {
            _integratorDutyPercent += gains.IntegralGain * error * elapsed.TotalSeconds;

            // A hard clamp on the integrator itself, independent of the output clamp below. The output
            // anti-windup handles saturation; this bounds the state so a pathological run cannot produce a
            // number that takes minutes to unwind even once saturation ends.
            _integratorDutyPercent = Math.Clamp(_integratorDutyPercent, -100d, 100d);
        }

        var result = proportional + _integratorDutyPercent;
        return double.IsFinite(result) ? result : 0d;
    }

    private double ComputeLead(double temperatureCelsius, TimeSpan elapsed)
    {
        if (_previousTemperatureCelsius is not double previous || elapsed <= TimeSpan.Zero)
        {
            _previousTemperatureCelsius = temperatureCelsius;
            return 0d;
        }

        var slope = (temperatureCelsius - previous) / elapsed.TotalSeconds;
        _previousTemperatureCelsius = temperatureCelsius;

        var smoothed = _slopeFilter.Sample(slope, elapsed) ?? 0d;
        _lastTemperatureSlope = smoothed;

        // Only RISING temperature contributes. A falling temperature is already being handled by the terms
        // that are unwinding; subtracting duty for it would make the fan undershoot and then have to come
        // back, which reads as hunting.
        if (smoothed <= 0d)
        {
            return 0d;
        }

        var capped = Math.Min(smoothed, MaximumLeadSlopeCelsiusPerSecond);
        return capped * LeadDutyPerCelsiusPerSecond;
    }

    private double UpdateThrottleLatch(
        ControlTelemetrySample controlTelemetry,
        double error,
        TimeSpan elapsed,
        DateTimeOffset timestamp)
    {
        var performanceRatio = controlTelemetry?.CpuPerformanceRatio;
        var isThrottlingNow = performanceRatio is double ratio
            && double.IsFinite(ratio)
            && ratio < ThrottlePerformanceRatioThreshold;

        if (isThrottlingNow)
        {
            _consecutiveThrottleSamples++;
            if (_consecutiveThrottleSamples >= ThrottleLatchSampleThreshold && !_isThrottleLatched)
            {
                _isThrottleLatched = true;
                _throttleLatchedAt = timestamp;
            }

            // Any throttling restarts the below-target clock, latched or not.
            _belowTargetDuration = TimeSpan.Zero;
        }
        else
        {
            _consecutiveThrottleSamples = 0;
        }

        if (!_isThrottleLatched)
        {
            return 0d;
        }

        // The release clock only advances while the temperature is actually below target, so a machine that
        // sits pinned at target keeps the escalation instead of dropping it on a timer.
        if (!isThrottlingNow && error < 0d && elapsed > TimeSpan.Zero)
        {
            _belowTargetDuration += elapsed;
            if (_belowTargetDuration >= ThrottleLatchReleaseWindow)
            {
                ReleaseThrottleLatch();
                return 0d;
            }
        }
        else if (error >= 0d)
        {
            _belowTargetDuration = TimeSpan.Zero;
        }

        return ThrottleEscalationDutyPercent;
    }

    private static double ApplyLimits(double rawDutyPercent, FanCalibrationSnapshot calibration, AdaptiveFanSettings settings)
    {
        if (!double.IsFinite(rawDutyPercent))
        {
            return 0d;
        }

        var duty = Math.Clamp(rawDutyPercent, 0d, 100d);

        if (settings.SafetyFloorEnabled)
        {
            duty = Math.Max(duty, settings.SafetyFloorPercent);
        }

        // Stall guard, applied after the user's floor and independent of it. A fan commanded between "off"
        // and "the slowest it can actually turn" does not spin slowly — it buzzes, or stops while still being
        // told to run. Snap to whichever end is nearer so the fan is either genuinely off or genuinely
        // turning. This is a mechanical fact of the hardware and is not the user's to override.
        var minimumSpin = calibration.MinimumSpinDutyPercent;
        if (minimumSpin > 0d && duty > 0d && duty < minimumSpin)
        {
            duty = duty >= minimumSpin / 2d ? minimumSpin : 0d;
        }

        return Math.Clamp(duty, 0d, 100d);
    }

    /// <summary>
    /// Limits how fast the commanded duty may move, asymmetrically.
    /// </summary>
    /// <remarks>
    /// Applied to the COMMAND, not to any single term, because the ear responds to the fan, not to the
    /// controller's internals. The first command after a reset is adopted verbatim — ramping up from zero on
    /// entry to Adaptive would leave the fan under-cooling for the length of the ramp.
    /// </remarks>
    private double ApplySlewLimit(double dutyPercent, TimeSpan elapsed)
    {
        if (_lastCommandedDutyPercent is not double previous || elapsed <= TimeSpan.Zero)
        {
            _lastCommandedDutyPercent = dutyPercent;
            return dutyPercent;
        }

        var seconds = elapsed.TotalSeconds;
        var maximumRise = MaximumRiseDutyPointsPerSecond * seconds;
        var maximumFall = MaximumFallDutyPointsPerSecond * seconds;

        var limited = Math.Clamp(dutyPercent, previous - maximumFall, previous + maximumRise);
        _lastCommandedDutyPercent = limited;
        return limited;
    }

    /// <summary>
    /// Bleeds saturated excess out of the integrator, over a tracking time constant.
    /// </summary>
    /// <remarks>
    /// The tracking constant is the integral time: unwinding at the same rate the loop integrates keeps the
    /// two in proportion, so the correction is never faster than the accumulation that caused it. A faster
    /// constant makes the integrator twitchy at every clamp; a slower one lets it wind up anyway.
    /// </remarks>
    private void UnwindIntegratorIfSaturated(double raw, double limited, AdaptivePidGains gains, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero || !gains.IsUsable)
        {
            return;
        }

        var excess = raw - limited;
        if (Math.Abs(excess) <= double.Epsilon)
        {
            return;
        }

        var trackingFraction = Math.Min(1d, elapsed.TotalSeconds / gains.IntegralTimeSeconds);
        _integratorDutyPercent -= excess * trackingFraction;
        _integratorDutyPercent = Math.Clamp(_integratorDutyPercent, -100d, 100d);
    }

    private static double? ToSetpointRpm(double dutyPercent, FanCalibrationSnapshot calibration)
    {
        if (calibration.MaximumRpm <= 0d || !double.IsFinite(calibration.MaximumRpm))
        {
            return null;
        }

        if (dutyPercent <= 0d)
        {
            return 0d;
        }

        // Linear duty→RPM. The EC's own loop is what actually holds the speed, so this only has to land in
        // the right neighbourhood; the inner loop absorbs the real curve's non-linearity. Floored at the
        // measured minimum spin so a low demand asks for a speed the fan can actually hold.
        var rpm = dutyPercent / 100d * calibration.MaximumRpm;
        return Math.Clamp(Math.Max(rpm, calibration.MinimumSpinRpm), 0d, calibration.MaximumRpm);
    }
}
