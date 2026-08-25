namespace SubZeroFramework.Service.Models;

/// <summary>
/// How long each phase of a calibration run lasts.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the runner so the durations can be shortened under test. A run at production timings takes
/// the better part of five minutes, which is right for a hot test on real hardware and impossible for a test
/// suite — and the alternative, trusting the sequencing because it is too slow to exercise, is worse.
/// </para>
/// <para>
/// The defaults are physics, not preference. They are sized against the thermal time constant of a laptop
/// chassis: tens of seconds to move heat from die to sensor, minutes for the chassis to reach equilibrium.
/// Shortening them on real hardware does not make calibration faster, it makes it wrong — the fit would read
/// a transient as a steady state.
/// </para>
/// </remarks>
public sealed record FanCalibrationTimings
{
    /// <summary>The timings a real run uses.</summary>
    public static FanCalibrationTimings Default { get; } = new();

    /// <summary>How long to sit at idle before starting, so the baseline is not the tail of something else.</summary>
    public TimeSpan IdleSettle { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The longest the run waits for the loaded temperature to stop climbing before proceeding anyway.</summary>
    public TimeSpan LoadSettleTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long the temperature fall after the step is recorded for.
    /// </summary>
    /// <remarks>
    /// Must comfortably exceed the chassis time constant. The fit reads its asymptote from the tail of this
    /// window, so cutting it short does not merely lose resolution — it reports a temperature still falling as
    /// the settled one, and every derived quantity inherits the error.
    /// </remarks>
    public TimeSpan Response { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often readings are taken and progress is reported.</summary>
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a stretch of readings has to hold still before the machine counts as settled.</summary>
    public TimeSpan SettleWindow { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The shortest time under load before settling may be declared at all.</summary>
    public TimeSpan MinimumLoad { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>How long to hold each duty while walking down to find the stall point.</summary>
    /// <remarks>
    /// Must span SEVERAL telemetry ticks, not one. The walk reads the tachometer after the dwell, and the
    /// primary tier can be as slow as two seconds — so a dwell of the same order can hand back a reading taken
    /// before the duty was even commanded. It shows the previous, higher duty still turning, the walk steps
    /// past the real stall point, and the fan ends up with a floor it cannot actually hold.
    /// </remarks>
    public TimeSpan MinimumSpinDwell { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>How long to wait for the fan to reach a commanded speed before judging whether it tracks.</summary>
    public TimeSpan TrackingSettle { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to hold each level of the gain sweep.
    /// </summary>
    /// <remarks>
    /// Null in production, where it is derived from the time constant the run has just measured — a couple of
    /// time constants is what makes the level's asymptote extrapolatable, and that is a property of the
    /// machine rather than a number to pick in advance. Set explicitly by tests, whose simulated chassis
    /// responds in milliseconds.
    /// </remarks>
    public TimeSpan? GainCurveDwell { get; init; }
}
