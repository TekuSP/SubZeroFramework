namespace SubZeroFramework.Models;

/// <summary>
/// The user-facing knobs of Adaptive mode for one fan: what temperature to hold, and whether the fan is
/// allowed to stop.
/// </summary>
/// <remarks>
/// Deliberately small. Everything else the controller needs is either measured
/// (<see cref="FanCalibrationSnapshot"/>) or live telemetry — the whole point of Adaptive is that the user
/// states an outcome rather than drawing a curve.
/// </remarks>
public sealed record AdaptiveFanSettings
{
    /// <summary>The coolest target the UI offers, in °C.</summary>
    public const double MinimumTargetCelsius = 60d;

    /// <summary>The quietest target the UI offers, in °C.</summary>
    public const double MaximumTargetCelsius = 95d;

    /// <summary>
    /// The highest target a fan driven by these sensors can usefully be asked to hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes the LOWEST firmware warning point across the driving sensors, not the highest. The fan is
    /// holding all of them, so the first sensor to complain is the one that binds — picking the highest would
    /// let a target sit above a limit some other sensor is already acting on.
    /// </para>
    /// <para>
    /// A target above the firmware's warning point is one the machine will never be left holding: the loop
    /// settles there and the firmware immediately intervenes, and the fan behaviour that follows reads as
    /// this app misbehaving. Clamped into the offered range so a sensor warning below 60 °C cannot collapse
    /// the slider to a single point.
    /// </para>
    /// </remarks>
    /// <param name="drivingSensorWarnCelsius">
    /// Firmware warning points for the sensors driving the fan. Empty where none reports one.
    /// </param>
    public static double ResolveTargetCeilingCelsius(IEnumerable<double> drivingSensorWarnCelsius)
    {
        ArgumentNullException.ThrowIfNull(drivingSensorWarnCelsius);

        var lowest = drivingSensorWarnCelsius
            .Where(static celsius => double.IsFinite(celsius))
            .DefaultIfEmpty(MaximumTargetCelsius)
            .Min();

        return Math.Clamp(lowest, MinimumTargetCelsius, MaximumTargetCelsius);
    }

    /// <summary>
    /// Where a fan starts before the user touches anything: warm enough to stay quiet on a laptop, well
    /// clear of the throttle point on every Framework platform.
    /// </summary>
    public const double DefaultTargetCelsius = 78d;

    /// <summary>
    /// Where the floor starts: above a typical stall point, low enough to be inaudible on an idle machine.
    /// </summary>
    public const double DefaultSafetyFloorPercent = 24d;

    /// <summary>The highest floor the UI offers, in duty percent.</summary>
    public const double MaximumSafetyFloorPercent = 60d;

    /// <summary>
    /// Settings for a fan the user has not configured, and what "Reset to defaults" restores.
    /// </summary>
    /// <remarks>
    /// Deliberately the same object for both. A reset that landed somewhere a fresh fan never starts would be
    /// a discrepancy nobody notices for months, and then only as "why is this fan different from that one?".
    /// </remarks>
    public static AdaptiveFanSettings Default { get; } = new();

    /// <summary>The driving temperature the controller holds, in canonical °C.</summary>
    public double TargetTemperatureCelsius { get; init; } = DefaultTargetCelsius;

    /// <summary>
    /// Whether the fan is held above <see cref="SafetyFloorPercent"/> even when the machine is cold.
    /// </summary>
    /// <remarks>
    /// On by default. A fan that never fully stops is the conservative choice, and a machine whose fan goes
    /// silent at idle reads as a fault to most people even when it is working exactly as asked.
    /// </remarks>
    public bool SafetyFloorEnabled { get; init; } = true;

    /// <summary>The floor, in duty percent. Only meaningful while <see cref="SafetyFloorEnabled"/>.</summary>
    public double SafetyFloorPercent { get; init; } = DefaultSafetyFloorPercent;

    /// <summary>
    /// λ — the closed-loop time constant, in seconds. The single tuning knob; see
    /// <see cref="Services.Control.AdaptivePidTuning"/>.
    /// </summary>
    /// <remarks>
    /// A SETTING rather than part of the calibration, because changing it does not require re-measuring the
    /// machine: the plant is unchanged, only how hard the loop is asked to chase it. Low is quick and
    /// restless, high is calm and slow to react.
    /// </remarks>
    public double LambdaSeconds { get; init; } = Services.Control.AdaptivePidTuning.DefaultLambdaSeconds;

    /// <summary>Returns these settings with every field pulled into the range the UI and EC accept.</summary>
    /// <remarks>
    /// Applied at the controller rather than trusted from the wire: these values cross a process boundary
    /// and reach an EC write, so a client that sends a target of 400 °C must produce a clamped fan, not a
    /// fan that never spins.
    /// </remarks>
    public AdaptiveFanSettings Sanitized()
        => this with
        {
            TargetTemperatureCelsius = double.IsFinite(TargetTemperatureCelsius)
                ? Math.Clamp(TargetTemperatureCelsius, MinimumTargetCelsius, MaximumTargetCelsius)
                : DefaultTargetCelsius,
            SafetyFloorPercent = double.IsFinite(SafetyFloorPercent)
                ? Math.Clamp(SafetyFloorPercent, 0d, MaximumSafetyFloorPercent)
                : 0d,
            LambdaSeconds = Services.Control.AdaptivePidTuning.ClampLambda(LambdaSeconds),
        };
}
