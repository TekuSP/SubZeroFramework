namespace SubZeroFramework.Models;

/// <summary>
/// One tick's worth of plant conditions, offered to the online learner.
/// </summary>
/// <remarks>
/// A record rather than a long parameter list because every field here is a QUALIFICATION criterion as much as
/// a measurement — the learner rejects most ticks, and naming each condition at the call site is what makes
/// the rejection rules auditable.
/// </remarks>
public sealed record AdaptiveLearningObservation
{
    /// <summary>The thermal load at this tick, in watts — whatever <see cref="ThermalLoadSource"/> says it is.</summary>
    public required double PackagePowerWatts { get; init; }

    /// <summary>
    /// Where that figure came from.
    /// </summary>
    /// <remarks>
    /// The estimator refuses samples whose source differs from the one its fit was built on. Component power
    /// and system power have completely different couplings to zone temperature, so mixing them would corrupt
    /// the fit the moment a charger was unplugged.
    /// </remarks>
    public required ThermalLoadSource ThermalLoadSource { get; init; }

    /// <summary>The absolute driving temperature, in °C. The dependent variable of the plant fit.</summary>
    public required double TemperatureCelsius { get; init; }

    /// <summary>Driving temperature minus target, in °C. Near zero means the loop has converged.</summary>
    public required double TemperatureErrorCelsius { get; init; }

    /// <summary>The duty actually commanded this tick, in percent. The regressor the fit solves K from.</summary>
    public required double CommandedDutyPercent { get; init; }

    /// <summary>Filtered rate of change of the driving temperature, in °C/s. Near zero means settled.</summary>
    public required double TemperatureSlopeCelsiusPerSecond { get; init; }

    /// <summary>The feed-forward term's contribution at this tick, in duty points.</summary>
    public required double FeedForwardDutyPercent { get; init; }

    /// <summary>The PI term's contribution at this tick, in duty points. This is the error measurement.</summary>
    public required double ProportionalIntegralDutyPercent { get; init; }

    /// <summary>True when the controller's demand was clipped, so the loop is not holding the temperature.</summary>
    public required bool IsSaturated { get; init; }

    /// <summary>True while a throttle escalation is adding duty the model did not ask for.</summary>
    public required bool IsThrottleLatched { get; init; }
}
