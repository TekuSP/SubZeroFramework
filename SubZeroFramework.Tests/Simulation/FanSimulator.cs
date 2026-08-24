namespace SubZeroFramework.Tests.Simulation;

/// <summary>
/// A deterministic fan: duty in, RPM out, with a stall threshold, start/stop hysteresis, a non-linear
/// duty→RPM curve and first-order spin-up. It is the actuator the calibration procedure characterises in its
/// first steps and the inner loop the cascade delegates to.
/// </summary>
/// <remarks>
/// Uses the same exact discretisation as <see cref="ThermalPlantSimulator"/> so results depend on elapsed
/// time rather than step size.
/// </remarks>
public sealed class FanSimulator
{
    private readonly FanSimulatorParameters _parameters;
    private readonly double _retainedFractionPerStep;
    private bool _isSpinning;

    public FanSimulator(FanSimulatorParameters parameters, TimeSpan timeStep)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeStep), timeStep, "The time step must be positive.");
        }

        if (parameters.StartDutyPercent < parameters.StallDutyPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                parameters.StartDutyPercent,
                "A fan cannot start at a lower duty than the one it stalls at.");
        }

        _parameters = parameters;
        TimeStep = timeStep;
        _retainedFractionPerStep = Math.Exp(-timeStep.TotalSeconds / parameters.ResponseTimeConstant.TotalSeconds);
    }

    public TimeSpan TimeStep { get; }

    /// <summary>Current speed. Zero means stalled.</summary>
    public double CurrentRpm { get; private set; }

    /// <summary>The duty actually applied on the last advance — the EC's choice when driven by commanded RPM.</summary>
    public double CurrentDutyPercent { get; private set; }

    public bool IsStalled => CurrentRpm <= 0d;

    /// <summary>Jumps to the steady state for a duty, so a test can start from a running fan.</summary>
    public void Settle(double dutyPercent)
    {
        CurrentDutyPercent = dutyPercent;
        _isSpinning = dutyPercent >= _parameters.StartDutyPercent;
        CurrentRpm = _isSpinning ? SteadyStateRpmForDuty(dutyPercent) : 0d;
    }

    /// <summary>Advances one step with a duty command and returns the new speed.</summary>
    public double AdvanceWithDuty(double dutyPercent)
    {
        CurrentDutyPercent = dutyPercent;

        // Hysteresis: a stopped fan needs StartDuty to break free, a turning one keeps going down to StallDuty.
        if (_isSpinning)
        {
            _isSpinning = dutyPercent >= _parameters.StallDutyPercent;
        }
        else if (dutyPercent >= _parameters.StartDutyPercent)
        {
            _isSpinning = true;
        }

        var target = _isSpinning ? SteadyStateRpmForDuty(dutyPercent) : 0d;
        CurrentRpm = target + ((CurrentRpm - target) * _retainedFractionPerStep);

        // Without this the rotor would coast toward zero forever and never read as stalled.
        if (!_isSpinning && CurrentRpm < 1d)
        {
            CurrentRpm = 0d;
        }

        return CurrentRpm;
    }

    /// <summary>
    /// Advances one step with an RPM command, as the cascade's inner loop would. The EC resolves it to a duty
    /// — correctly when it tracks RPM, and by a naive fraction-of-maximum when it does not.
    /// </summary>
    public double AdvanceWithCommandedRpm(double commandedRpm)
        => AdvanceWithDuty(DutyForCommandedRpm(commandedRpm));

    /// <summary>The duty the EC would pick for a commanded speed.</summary>
    public double DutyForCommandedRpm(double commandedRpm)
    {
        if (!_parameters.TracksCommandedRpm)
        {
            return Math.Clamp(100d * commandedRpm / _parameters.MaximumSpeedRpm, 0d, 100d);
        }

        if (commandedRpm < _parameters.MinimumSpinRpm)
        {
            return 0d;
        }

        var span = _parameters.MaximumSpeedRpm - _parameters.MinimumSpinRpm;
        var fraction = Math.Clamp((commandedRpm - _parameters.MinimumSpinRpm) / span, 0d, 1d);
        var dutySpan = 100d - _parameters.StallDutyPercent;
        return _parameters.StallDutyPercent + (Math.Pow(fraction, 1d / _parameters.Curvature) * dutySpan);
    }

    /// <summary>The speed this duty settles at, ignoring spin-up lag and hysteresis.</summary>
    public double SteadyStateRpmForDuty(double dutyPercent)
    {
        if (dutyPercent < _parameters.StallDutyPercent)
        {
            return 0d;
        }

        var dutySpan = 100d - _parameters.StallDutyPercent;
        var fraction = Math.Clamp((dutyPercent - _parameters.StallDutyPercent) / dutySpan, 0d, 1d);
        var span = _parameters.MaximumSpeedRpm - _parameters.MinimumSpinRpm;
        return _parameters.MinimumSpinRpm + (Math.Pow(fraction, _parameters.Curvature) * span);
    }
}
