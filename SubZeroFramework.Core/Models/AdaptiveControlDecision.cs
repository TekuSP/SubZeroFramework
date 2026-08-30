namespace SubZeroFramework.Models;

/// <summary>
/// One tick of the adaptive controller, broken into the terms that produced it.
/// </summary>
/// <remarks>
/// <para>
/// The decomposition is not diagnostics — it is the feature. A fan that changes speed for reasons the user
/// cannot see is indistinguishable from a broken fan, and the usual complaint about automatic fan control is
/// not that it is wrong but that it is unexplainable. The settings page renders these four terms as a stacked
/// bar, so "why did it just speed up?" has a literal answer.
/// </para>
/// <para>
/// Every term is in DUTY POINTS and they sum to <see cref="RawDutyPercent"/> before limiting.
/// <see cref="DutyPercent"/> is what is actually commanded, after the floor and the 0–100 clamp — the two are
/// separate so the UI can show a controller that is asking for more than the fan can deliver.
/// </para>
/// </remarks>
public sealed record AdaptiveControlDecision
{
    /// <summary>A tick that produced no actuation, because the fan is not adaptively driven right now.</summary>
    public static AdaptiveControlDecision NotDriven { get; } = new();

    /// <summary>False when the controller could not run — the caller must leave the EC alone.</summary>
    public bool IsDriven { get; init; }

    /// <summary>Anticipatory term from CPU package power: airflow for heat that has not arrived yet.</summary>
    public double FeedForwardDutyPercent { get; init; }

    /// <summary>Proportional + integral correction closing the remaining gap to target.</summary>
    public double ProportionalIntegralDutyPercent { get; init; }

    /// <summary>Extra duty while the driving temperature is still climbing.</summary>
    public double LeadDutyPercent { get; init; }

    /// <summary>Latched escalation after the CPU reported throttling.</summary>
    public double ThrottleEscalationDutyPercent { get; init; }

    /// <summary>The sum of the four terms, before the safety floor and the 0–100 clamp.</summary>
    public double RawDutyPercent { get; init; }

    /// <summary>What the controller actually commands, in duty percent.</summary>
    public double DutyPercent { get; init; }

    /// <summary>
    /// What speed <see cref="DutyPercent"/> is expected to produce, for display. Null when the fan has not
    /// been calibrated, so no speed can honestly be named.
    /// </summary>
    /// <remarks>
    /// An estimate of an outcome, NOT a demand — the fan is driven by duty. Computed by the service, which
    /// holds the calibration, so every client shows the same number.
    /// </remarks>
    public double? ExpectedRpm { get; init; }

    /// <summary>The driving temperature this decision reacted to.</summary>
    public double DrivingTemperatureCelsius { get; init; }

    /// <summary>The target it is holding.</summary>
    public double TargetTemperatureCelsius { get; init; }

    /// <summary>True while a throttle escalation is latched.</summary>
    public bool IsThrottleLatched { get; init; }

    /// <summary>When the latch engaged, for the UI's "reported throttling at {time}" line.</summary>
    public DateTimeOffset? ThrottleLatchedAt { get; init; }

    /// <summary>
    /// Seconds of continuous below-target running still required before the latch releases, or null when
    /// nothing is latched. Counts down only while the temperature is actually below target.
    /// </summary>
    public double? ThrottleLatchReleaseSeconds { get; init; }

    /// <summary>
    /// True when feed-forward had no power reading to work from, so the controller is running on feedback
    /// alone. Correct behaviour, but degraded: it can only react after the temperature moves.
    /// </summary>
    public bool IsFeedForwardUnavailable { get; init; }

    /// <summary>
    /// What continuous operation has taught this fan since its calibration run. Carried on every decision so
    /// the UI can show the model improving without a second stream.
    /// </summary>
    public AdaptiveLearningState Learning { get; init; } = AdaptiveLearningState.None;
}
