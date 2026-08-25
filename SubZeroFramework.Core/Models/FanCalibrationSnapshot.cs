namespace SubZeroFramework.Models;

/// <summary>
/// What a calibration run learned about one fan: a first-order-plus-dead-time (FOPDT) model of how that fan
/// moves temperature, the controller gains derived from it, and the fan's own mechanical limits.
/// </summary>
/// <remarks>
/// <para>
/// FOPDT is the standard model for this class of plant and is exactly what a step test identifies: step the
/// fan, watch the temperature, fit three numbers — gain, time constant, dead time.
/// </para>
/// <para>
/// The gains are STORED rather than recomputed every tick, deliberately. Recomputing would mean a future
/// change to the tuning law silently re-tunes every machine on upgrade; storing means the user keeps the
/// gains that were measured and verified against their hardware until they choose to recalibrate.
/// </para>
/// </remarks>
public sealed record FanCalibrationSnapshot
{
    /// <summary>An uncalibrated fan.</summary>
    public static FanCalibrationSnapshot None { get; } = new();

    /// <summary>
    /// The model Adaptive runs on before anything has been measured or identified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every value here errs in the direction that makes the loop CALMER, and that is not a matter of taste —
    /// it follows from the tuning rule. Since <c>Kc = τ / (K·(λ + L))</c>, overestimating K, underestimating τ
    /// and overestimating L each reduce the proportional gain. So a bootstrap that is wrong in these
    /// directions produces a sluggish fan, never an oscillating one, and identification then walks each value
    /// toward the truth from the safe side.
    /// </para>
    /// <para>
    /// Tracking is <see cref="FanSpeedTrackingMode.Duty"/> because cascade requires the hot test's verdict
    /// that the EC actually holds a commanded speed. Commanding RPM to a fan that does not track it would
    /// silently do nothing.
    /// </para>
    /// <para>
    /// The stall floor is a guess, so it is deliberately generous: a fan held slightly too fast is quieter
    /// than one buzzing at a duty it cannot turn at.
    /// </para>
    /// </remarks>
    public static FanCalibrationSnapshot Bootstrap { get; } = new()
    {
        State = FanCalibrationState.Bootstrap,

        // Overestimated: assume the fan is more effective than it probably is, so gains come out low.
        ProcessGainCelsiusPerPercent = 0.8d,

        // Underestimated: assume the machine responds faster than it probably does.
        TimeConstantSeconds = 15d,

        // Overestimated: assume more delay than there probably is.
        DeadTimeSeconds = 8d,

        // Under-predicting feed-forward is safe — PI covers the shortfall. Over-predicting overshoots.
        FeedForwardDutyPerWatt = 0.3d,

        MinimumSpinDutyPercent = 20d,
        MinimumSpinRpm = 0d,
        MaximumRpm = 0d,
        TrackingMode = FanSpeedTrackingMode.Duty,
    };

    /// <summary>How much of this model is usable; see <see cref="IsUsable"/> for the honest check.</summary>
    public FanCalibrationState State { get; init; } = FanCalibrationState.None;

    /// <summary>When the run that produced this model completed.</summary>
    public DateTimeOffset? CalibratedAt { get; init; }

    /// <summary>
    /// Process gain K: °C of steady-state cooling per 1 percentage point of extra duty, as a POSITIVE
    /// magnitude. Larger means a more effective fan.
    /// </summary>
    public double ProcessGainCelsiusPerPercent { get; init; }

    /// <summary>Time constant τ, in seconds: how long temperature takes to cover ~63% of a change.</summary>
    public double TimeConstantSeconds { get; init; }

    /// <summary>
    /// Dead time L, in seconds: the delay between commanding a speed and temperature moving at all. This is
    /// what limits how aggressive the controller may safely be — see <see cref="Services.Control.AdaptivePidTuning"/>.
    /// </summary>
    public double DeadTimeSeconds { get; init; }

    /// <summary>The lowest speed the fan reliably keeps turning at. Below this it stalls.</summary>
    public double MinimumSpinRpm { get; init; }

    /// <summary>The duty corresponding to <see cref="MinimumSpinRpm"/>; the default safety floor.</summary>
    public double MinimumSpinDutyPercent { get; init; }

    /// <summary>The fastest speed observed at 100% duty, used to turn a duty demand into an RPM setpoint.</summary>
    public double MaximumRpm { get; init; }

    /// <summary>Proportional gain, in duty points per °C of error.</summary>
    public double ProportionalGain { get; init; }

    /// <summary>Integral gain, in duty points per °C-second of accumulated error.</summary>
    public double IntegralGain { get; init; }

    /// <summary>
    /// Feed-forward gain: duty points per watt of CPU package power. Derived at calibration from the duty
    /// actually needed to hold target at the measured load, so it already carries this chassis's thermal
    /// resistance rather than a generic constant.
    /// </summary>
    public double FeedForwardDutyPerWatt { get; init; }

    /// <summary>Whether the EC tracked commanded speeds during verification.</summary>
    public FanSpeedTrackingMode TrackingMode { get; init; } = FanSpeedTrackingMode.Duty;

    /// <summary>
    /// What the extra fan actually bought in sustained speed. See <see cref="FanPerformanceResponse"/>.
    /// </summary>
    /// <remarks>
    /// Carried alongside the thermal model rather than folded into it: nothing in the control loop reads
    /// this. It exists so the user can be told whether the noise is buying them anything, which is a question
    /// the temperature model on its own cannot answer.
    /// </remarks>
    public FanPerformanceResponse PerformanceResponse { get; init; } = FanPerformanceResponse.None;

    /// <summary>
    /// Cooling per duty point measured at several duties, so the controller can be tuned for the operating
    /// point it is actually at. See <see cref="FanGainCurve"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="PerformanceResponse"/>, this one IS read by the control loop — it is what makes the
    /// PI gains follow the fan's real, nonlinear response instead of a single average that is wrong at both
    /// ends of the range.
    /// </remarks>
    public FanGainCurve GainCurve { get; init; } = FanGainCurve.None;

    /// <summary>
    /// Whether these numbers came from an actual measurement of this machine.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="IsUsable"/>, and the two are not interchangeable. Usable asks "can the
    /// controller run on this?", which the built-in bootstrap satisfies. This asks "has anyone ever measured
    /// this fan?", which the bootstrap does not — it is the same guess on every machine in the world.
    /// </remarks>
    public bool IsMeasured => (State is FanCalibrationState.Ok or FanCalibrationState.Stale) && IsUsable;

    /// <summary>
    /// True when the model is complete enough to run a controller on.
    /// </summary>
    /// <remarks>
    /// Checked independently of <see cref="State"/>: a run that produced a zero or negative process gain is
    /// not usable no matter what state it claims, and a controller that divided by it would demand infinite
    /// duty. Callers gate on this, not on the enum.
    /// </remarks>
    public bool IsUsable
        => State is FanCalibrationState.Ok or FanCalibrationState.Stale or FanCalibrationState.Bootstrap
            && ProcessGainCelsiusPerPercent > 0d
            && double.IsFinite(ProcessGainCelsiusPerPercent)
            && TimeConstantSeconds > 0d
            && double.IsFinite(TimeConstantSeconds)
            && DeadTimeSeconds >= 0d
            && double.IsFinite(DeadTimeSeconds)
            && double.IsFinite(ProportionalGain)
            && double.IsFinite(IntegralGain);
}
