using System.Diagnostics;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// The schedule both load generators follow: start gently, climb to the target, then hold.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why ramp at all.</b> Slamming every core to its target the instant a calibration begins makes the
/// machine lurch — the very thing that makes a user cancel and never run it again. Climbing over half a
/// minute keeps the machine usable throughout, and gives the thermal system time to respond the way it would
/// to a real workload spinning up rather than to a step nothing else produces.
/// </para>
/// <para>
/// <b>Why it must finish before measuring.</b> The fit assumes load holds still while the fan is stepped. A
/// temperature climbing because the load is still growing looks exactly like a machine that has not settled,
/// and any moment of it can be mistaken for a plateau. So the ramp is a schedule the run can ask about, not
/// just a fade — <see cref="IsAtTarget"/> is what the run waits on before it believes anything.
/// </para>
/// </remarks>
public sealed class LoadRamp
{
    /// <summary>Where the ramp begins — light enough to be barely noticeable.</summary>
    public const double InitialFraction = 0.15d;

    /// <summary>
    /// Where the ramp ends. Deliberately short of everything the machine has, so it stays usable.
    /// </summary>
    /// <remarks>
    /// A calibration takes minutes. Consuming the whole machine for those minutes makes it an operation
    /// people avoid, and an avoided calibration is worth nothing. Four fifths still produces a firmly loaded
    /// machine — far more than the 25 W floor the run requires — while leaving enough headroom to work.
    /// </remarks>
    public const double DefaultTargetFraction = 0.8d;

    /// <summary>How long the climb takes on a real run.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(30);

    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    /// <param name="duration">
    /// How long the climb takes. Shortened by tests, which are checking that the ramp CONVERGES where it
    /// says it does — a property the duty-cycle correction reaches within a few cycles, so it needs seconds
    /// rather than the half-minute a real run spends being gentle with the user's machine.
    /// </param>
    /// <param name="targetFraction">
    /// Where the climb ends. Varied by tests, which prove the load actually LANDS where it is told rather
    /// than saturating whatever it was asked for — a bug a single set point can pass by coincidence.
    /// </param>
    public LoadRamp(TimeSpan? duration = null, double? targetFraction = null)
    {
        Duration = duration ?? DefaultDuration;
        TargetFraction = Math.Clamp(targetFraction ?? DefaultTargetFraction, 0.05d, 1d);
    }

    /// <summary>How long this ramp's climb takes.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Where this ramp's climb ends.</summary>
    public double TargetFraction { get; }

    /// <summary>Where the ramp has reached, linearly interpolated and clamped at the target.</summary>
    public double CurrentFraction
    {
        get
        {
            // Never climbs to somewhere above the target, and never starts above it either: a target BELOW
            // the initial fraction would otherwise ramp downward, which is not a ramp.
            var start = Math.Min(InitialFraction, TargetFraction);
            var progress = Math.Clamp(_elapsed.Elapsed / Duration, 0d, 1d);
            return start + ((TargetFraction - start) * progress);
        }
    }

    /// <summary>True once the climb is over and the load is steady enough to measure against.</summary>
    public bool IsAtTarget => _elapsed.Elapsed >= Duration;
}
