namespace SubZeroFramework.Tests.Simulation;

/// <summary>
/// Parameters of the first-order-plus-dead-time thermal plant the adaptive fan controller is designed
/// against: <c>T(s)/D(s) = −K·e^(−Ls)/(τs+1)</c>.
/// </summary>
/// <remarks>
/// Defaults are the Framework 16 figures quoted throughout the adaptive-fan design handoff — K 0.42 °C per
/// duty point, τ 26 s, L 4 s — so a test that says nothing about the plant is still exercising a realistic
/// one rather than a convenient one.
///
/// The plant gain is NEGATIVE (more airflow, less heat), but <see cref="CoolingDegreesPerDutyPercent"/> is
/// stored as a positive magnitude because that is how the number is measured, reported and shown to the user.
/// </remarks>
public sealed record ThermalPlantParameters
{
    /// <summary>Room temperature. The plant cannot settle below this — see <see cref="ThermalPlantSimulator.IsAtAmbientFloor"/>.</summary>
    public double AmbientCelsius { get; init; } = 22d;

    /// <summary>Steady-state rise per watt of dissipated heat at zero duty.</summary>
    public double DegreesPerWatt { get; init; } = 0.9d;

    /// <summary>Steady-state drop per duty point — the identified <c>K</c>.</summary>
    public double CoolingDegreesPerDutyPercent { get; init; } = 0.42d;

    /// <summary>How long the plant takes to cover 63.2% of a change once it starts moving — the identified <c>τ</c>.</summary>
    public TimeSpan TimeConstant { get; init; } = TimeSpan.FromSeconds(26);

    /// <summary>Transport delay before a change at the die or the fan shows up at the sensor — the identified <c>L</c>.</summary>
    public TimeSpan DeadTime { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Peak-to-peak sensor noise. Zero by default so fitting tests are exact unless they opt in.</summary>
    public double SensorNoiseCelsius { get; init; }

    /// <summary>Fixed so a noisy run is still reproducible — a flaky control test is worse than no test.</summary>
    public int NoiseSeed { get; init; } = 20260823;
}
