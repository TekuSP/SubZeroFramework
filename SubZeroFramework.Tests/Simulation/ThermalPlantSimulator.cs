namespace SubZeroFramework.Tests.Simulation;

/// <summary>
/// A deterministic first-order-plus-dead-time thermal plant, so the controller, the model fitter and the
/// step detector can be exercised across dead times, load steps and sensor noise without heating real
/// hardware — and without a five-minute wait per iteration.
/// </summary>
/// <remarks>
/// Advances in fixed steps and never reads the wall clock, so a run is reproducible and a whole hour of
/// plant time costs microseconds.
///
/// The first-order lag is integrated by EXACT discretisation (<c>e^(−dt/τ)</c>) rather than a forward-Euler
/// step. That matters: Euler's error grows with the step size, so a tuning test would silently measure the
/// integrator's inaccuracy instead of the controller's behaviour, and the 63.2%-at-τ property the two-point
/// fit relies on would not hold exactly.
///
/// Dead time is applied to BOTH inputs, because both act through the same transport path. That is what makes
/// the simulator a fair test of feed-forward: the controller reads power as it happens, but the plant only
/// shows it L seconds later, which is precisely the lag feed-forward exists to cover.
/// </remarks>
public sealed class ThermalPlantSimulator
{
    private readonly ThermalPlantParameters _parameters;
    private readonly Queue<(double DutyPercent, double HeatPowerWatts)> _transportDelay = new();
    private readonly Random _noise;
    private readonly double _retainedFractionPerStep;

    public ThermalPlantSimulator(ThermalPlantParameters parameters, TimeSpan timeStep)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeStep), timeStep, "The time step must be positive.");
        }

        if (parameters.TimeConstant <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.TimeConstant, "The time constant must be positive.");
        }

        if (parameters.DeadTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.DeadTime, "The dead time cannot be negative.");
        }

        _parameters = parameters;
        TimeStep = timeStep;
        _noise = new Random(parameters.NoiseSeed);
        _retainedFractionPerStep = Math.Exp(-timeStep.TotalSeconds / parameters.TimeConstant.TotalSeconds);

        // The delay line is quantised to whole steps, so a dead time that is not a multiple of the step size
        // is rounded to the nearest one. Callers that care about an exact L should pick a step that divides it.
        DeadTimeSteps = (int)Math.Round(parameters.DeadTime.TotalSeconds / timeStep.TotalSeconds, MidpointRounding.AwayFromZero);

        CoreTemperatureCelsius = parameters.AmbientCelsius;
        SensedTemperatureCelsius = parameters.AmbientCelsius;
        FillTransportDelay(0d, 0d);
    }

    /// <summary>The fixed interval one <see cref="Advance"/> call represents.</summary>
    public TimeSpan TimeStep { get; }

    /// <summary>How many steps the transport delay line holds — the quantised dead time.</summary>
    public int DeadTimeSteps { get; }

    /// <summary>Plant time since construction, advanced only by <see cref="Advance"/>.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>The true plant temperature, free of sensor noise. Assert against this when noise is irrelevant.</summary>
    public double CoreTemperatureCelsius { get; private set; }

    /// <summary>What a sensor would report — the core temperature plus noise. This is what a controller may read.</summary>
    public double SensedTemperatureCelsius { get; private set; }

    /// <summary>
    /// True when the equilibrium has been clamped at ambient, which means the plant is no longer the linear
    /// model the controller assumes. A fitting test that trips this is measuring the clamp, not the plant —
    /// assert it stays false rather than discovering the problem as an unexplained bad fit.
    /// </summary>
    public bool IsAtAmbientFloor { get; private set; }

    /// <summary>
    /// Jumps straight to the steady state for the given inputs and fills the delay line with them, so a test
    /// can begin from a settled plant instead of simulating the approach to one.
    /// </summary>
    public void Settle(double dutyPercent, double heatPowerWatts)
    {
        FillTransportDelay(dutyPercent, heatPowerWatts);
        IsAtAmbientFloor = ComputeRise(dutyPercent, heatPowerWatts) < 0d;
        CoreTemperatureCelsius = ComputeEquilibrium(dutyPercent, heatPowerWatts);
        SensedTemperatureCelsius = CoreTemperatureCelsius;
    }

    /// <summary>
    /// Advances the plant by one <see cref="TimeStep"/> and returns the newly sensed temperature.
    /// </summary>
    public double Advance(double dutyPercent, double heatPowerWatts)
    {
        _transportDelay.Enqueue((dutyPercent, heatPowerWatts));
        var (appliedDuty, appliedPower) = _transportDelay.Dequeue();

        IsAtAmbientFloor = ComputeRise(appliedDuty, appliedPower) < 0d;

        var equilibrium = ComputeEquilibrium(appliedDuty, appliedPower);
        CoreTemperatureCelsius = equilibrium + ((CoreTemperatureCelsius - equilibrium) * _retainedFractionPerStep);

        SensedTemperatureCelsius = _parameters.SensorNoiseCelsius > 0d
            ? CoreTemperatureCelsius + (((_noise.NextDouble() * 2d) - 1d) * (_parameters.SensorNoiseCelsius / 2d))
            : CoreTemperatureCelsius;

        Elapsed += TimeStep;
        return SensedTemperatureCelsius;
    }

    /// <summary>
    /// The temperature the plant would settle at for these inputs, floored at ambient because no amount of
    /// airflow cools a machine below the room it sits in.
    /// </summary>
    public double ComputeEquilibrium(double dutyPercent, double heatPowerWatts)
        => _parameters.AmbientCelsius + Math.Max(0d, ComputeRise(dutyPercent, heatPowerWatts));

    private double ComputeRise(double dutyPercent, double heatPowerWatts)
        => (heatPowerWatts * _parameters.DegreesPerWatt)
            - (dutyPercent * _parameters.CoolingDegreesPerDutyPercent);

    private void FillTransportDelay(double dutyPercent, double heatPowerWatts)
    {
        _transportDelay.Clear();
        for (var step = 0; step < DeadTimeSteps; step++)
        {
            _transportDelay.Enqueue((dutyPercent, heatPowerWatts));
        }
    }
}
