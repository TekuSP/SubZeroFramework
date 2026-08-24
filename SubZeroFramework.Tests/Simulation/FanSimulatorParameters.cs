namespace SubZeroFramework.Tests.Simulation;

/// <summary>
/// Parameters of the simulated fan — the actuator between the controller's output and the thermal plant's
/// input. Defaults follow the adaptive-fan design handoff, where minimum spin was measured at 1,180 RPM (17%).
/// </summary>
public sealed record FanSimulatorParameters
{
    /// <summary>Speed at 100% duty.</summary>
    public double MaximumSpeedRpm { get; init; } = 7000d;

    /// <summary>Speed at <see cref="StallDutyPercent"/> — the slowest the fan can turn without stopping.</summary>
    public double MinimumSpinRpm { get; init; } = 1180d;

    /// <summary>A turning fan stops below this duty.</summary>
    public double StallDutyPercent { get; init; } = 17d;

    /// <summary>
    /// A stopped fan needs more duty to break static friction than to keep turning, so it only starts at or
    /// above this. Modelling the hysteresis is what lets a test tell a correct min-spin search (ramp DOWN
    /// while turning) from one that ramps up from rest and reports the higher, wrong number.
    /// </summary>
    public double StartDutyPercent { get; init; } = 22d;

    /// <summary>
    /// Shape of the duty→RPM curve above the stall point. 1 is linear; above 1 bows the curve so that low duty
    /// buys less airflow than a naive linear reading would assume, which is the point of measuring it at all.
    /// </summary>
    public double Curvature { get; init; } = 1.4d;

    /// <summary>How quickly the rotor reaches a new speed. Short next to the thermal plant, but not zero.</summary>
    public TimeSpan ResponseTimeConstant { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Whether the EC honours a commanded RPM. When false it treats the request as a plain fraction of maximum
    /// speed and applies that as duty, which lands wide of the mark on a non-linear fan — the "duty fallback"
    /// case the calibration's tracking check exists to detect.
    /// </summary>
    public bool TracksCommandedRpm { get; init; } = true;
}
