namespace SubZeroFramework.Service.Services;

/// <summary>
/// Learns what a sleep actually costs on THIS machine, and sizes both the work and the idle around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Calculating the sleep does not work.</b> A request is served at the scheduler's own granularity, which
/// differs by operating system, by machine, and over time on one machine as other software raises and lowers
/// the timer resolution. Asking for 23 ms and being given 31 is an ordinary Windows outcome.
/// </para>
/// <para>
/// <b>And adjusting only the sleep cannot work either.</b> There is a shortest sleep the machine will serve —
/// on Windows typically about 15.6 ms — so a fixed chunk of work has a ceiling on the duty it can reach:
/// burning 10 ms and then idling for the shortest possible sleep is 39% busy, and no amount of asking for
/// less idle goes higher. Every target above that ceiling can only be met by working LONGER, not idling less.
/// </para>
/// <para>
/// So this learns the shortest idle the machine actually delivers, and sizes the work against it:
/// <c>burn = quantum · target/(1−target)</c>. A residual trim then corrects whatever that arithmetic misses,
/// measured over a window rather than assumed. Together they converge on any scheduler without this code
/// knowing anything about it.
/// </para>
/// <para>
/// It starts DELIBERATELY GENEROUS — a short burn against a full sleep — so load approaches from below.
/// Overshooting the first window is the one direction a user notices, because it happens exactly when they
/// have just started the run and are watching.
/// </para>
/// </remarks>
public sealed class AdaptiveDutyLimiter
{
    /// <summary>How much of the residual correction to apply per window.</summary>
    private const double Damping = 0.5d;

    /// <summary>How far one window may move the burn duration, as a multiple.</summary>
    private const double MaximumAdjustmentRatio = 2d;

    /// <summary>Assumed shortest sleep until the machine demonstrates otherwise.</summary>
    /// <remarks>
    /// The Windows default tick. Only a starting guess — the first real sleep replaces it with whatever this
    /// machine actually does, which on a system with a raised timer resolution is far smaller.
    /// </remarks>
    private static readonly TimeSpan AssumedQuantum = TimeSpan.FromMilliseconds(15.6d);

    /// <summary>
    /// What is asked for when idling: deliberately less than any scheduler will grant.
    /// </summary>
    /// <remarks>
    /// Asking for the smallest useful amount is what makes the machine reveal its own floor — whatever comes
    /// back IS the granularity, with no probing and no per-platform table. Asking for the quantum already
    /// believed in would only ever confirm it, so a machine with a raised timer resolution would be driven
    /// as though it were sixteen times coarser than it is.
    /// </remarks>
    private static readonly TimeSpan MinimalSleepRequest = TimeSpan.FromMilliseconds(1d);

    /// <summary>Longest slice of work permitted regardless of granularity.</summary>
    /// <remarks>
    /// Past this the machine stops feeling like it has a background load and starts feeling like it freezes
    /// periodically, which is worse than missing the target.
    /// </remarks>
    private static readonly TimeSpan AbsoluteMaximumBurn = TimeSpan.FromMilliseconds(400d);

    private readonly TimeSpan _window;
    private readonly TimeSpan _minimumBurn;

    private TimeSpan _quantum = AssumedQuantum;
    private TimeSpan _busy;
    private TimeSpan _idle;
    private int _cycles;
    private double _trim = 1d;
    private bool _windowRequestedMinimalSleep = true;

    public AdaptiveDutyLimiter(TimeSpan minimumBurn, TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromMilliseconds(250);
        _minimumBurn = minimumBurn;

        BurnFor = minimumBurn;
    }

    /// <summary>
    /// The longest slice of work worth scheduling, given how coarse this machine's sleeps turned out to be.
    /// </summary>
    /// <remarks>
    /// Scaled to the quantum rather than fixed, because the work needed to reach a target is proportional to
    /// it: a target of 0.9 against a 15.6 ms floor needs a 140 ms burn, and a cap chosen without reference to
    /// the granularity would silently make high targets unreachable on coarse machines while being needlessly
    /// permissive on fine ones.
    /// </remarks>
    private TimeSpan MaximumBurn
    {
        get
        {
            var scaled = _quantum * 15d;
            if (scaled > AbsoluteMaximumBurn)
            {
                scaled = AbsoluteMaximumBurn;
            }

            return scaled < _minimumBurn ? _minimumBurn : scaled;
        }
    }

    /// <summary>How long to work before idling.</summary>
    public TimeSpan BurnFor { get; private set; }

    /// <summary>How long to ask to idle for after each slice of work.</summary>
    /// <remarks>
    /// See <see cref="Record"/> for why this and <see cref="BurnFor"/> take turns being the lever.
    /// </remarks>
    /// <remarks>
    /// Starts at the minimum so the very first window measures the machine's floor, which every later
    /// decision depends on. Combined with the shortest slice of work, that is also the lowest duty this can
    /// produce — so the load approaches its target from below, which is the direction a user does not notice.
    /// </remarks>
    public TimeSpan SleepFor { get; private set; } = MinimalSleepRequest;

    /// <summary>The duty measured over the last completed window, 0–1.</summary>
    public double ObservedFraction { get; private set; }

    /// <summary>What a sleep request actually costs on this machine, measured rather than assumed.</summary>
    public TimeSpan ObservedQuantum => _quantum;

    /// <summary>Whether a full window has been measured yet.</summary>
    public bool HasMeasurement { get; private set; }

    /// <summary>
    /// Records one burn-and-idle cycle, re-sizing the work once a window's worth has accumulated.
    /// </summary>
    /// <param name="busy">How long the cycle spent working.</param>
    /// <param name="idle">How long it actually idled — the measured cost of the request, not the request.</param>
    /// <param name="targetFraction">The share of time that should be spent working.</param>
    public void Record(TimeSpan busy, TimeSpan idle, double targetFraction)
    {
        _busy += busy;
        _idle += idle;
        _cycles++;

        var elapsed = _busy + _idle;
        if (elapsed < _window)
        {
            return;
        }

        ObservedFraction = elapsed > TimeSpan.Zero ? _busy / elapsed : 0d;
        HasMeasurement = true;

        // What the SHORTEST POSSIBLE sleep costs on this machine — the floor the regime choice below turns
        // on. Two conditions, both load-bearing:
        //
        // The average over the window, not the shortest idle ever seen: one spuriously fast wakeup would drag
        // a running minimum down permanently, and every slice sized against it would be far too short.
        //
        // And only from windows that actually REQUESTED the minimum. In the low-target regime this asks for
        // long sleeps on purpose; folding those in would measure its own request rather than the machine's
        // floor, and the two regimes would then chase each other indefinitely.
        if (_windowRequestedMinimalSleep && _cycles > 0 && _idle > TimeSpan.Zero)
        {
            _quantum = _idle / _cycles;
        }

        if (targetFraction is <= 0d or >= 1d)
        {
            // BOTH sides are set. Leaving the sleep at whatever the previous regime asked for meant a target
            // of 1.0 still idled — a full-load request that quietly did not deliver full load.
            BurnFor = targetFraction >= 1d ? MaximumBurn : _minimumBurn;
            SetSleep(targetFraction >= 1d ? TimeSpan.Zero : MinimalSleepRequest);
            Reset();
            return;
        }

        // Correct whatever the arithmetic missed, from what the window actually measured.
        if (ObservedFraction > 0d)
        {
            var ratio = Math.Clamp(targetFraction / ObservedFraction, 1d / MaximumAdjustmentRatio, MaximumAdjustmentRatio);
            _trim = Math.Clamp(_trim * (1d + ((ratio - 1d) * Damping)), 0.1d, 10d);
        }

        // ONE rule for every target, because a machine only offers idle in whole multiples of its floor.
        //
        // Holding the work at a fixed length and varying only the idle makes most targets unreachable: with a
        // 10 ms slice against a 15.6 ms floor the achievable duties are 39%, 24%, 18% — and nothing between.
        // Asking for 30% would land on whichever of those was nearest and stay there, looking like a slow
        // drift rather than a wall.
        //
        // So the idle is chosen in whole quanta, and the work is sized against THAT. Enough quanta are taken
        // for the matching work to clear the minimum worth scheduling; one is enough for high targets, more
        // as the target falls. Every duty in between becomes reachable because both sides move.
        var workPerIdle = targetFraction / (1d - targetFraction);
        var quanta = 1d;

        if (_quantum > TimeSpan.Zero && workPerIdle > 0d)
        {
            quanta = Math.Max(1d, Math.Ceiling(_minimumBurn / (_quantum * workPerIdle)));
        }

        var idleTarget = _quantum * quanta;
        var ideal = idleTarget * workPerIdle * _trim;

        var maximum = MaximumBurn;
        BurnFor = ideal < _minimumBurn ? _minimumBurn : ideal > maximum ? maximum : ideal;

        // One quantum is requested as the bare minimum, which is also the only case that can measure the
        // floor. More than one is asked for just under the boundary, so it lands in the intended multiple
        // rather than being rounded up into the next.
        SetSleep(quanta <= 1d ? MinimalSleepRequest : idleTarget * 0.9d);

        Reset();
    }

    /// <summary>Sets the sleep request, remembering whether it was the minimum so the floor stays measurable.</summary>
    private void SetSleep(TimeSpan sleep)
    {
        SleepFor = sleep;
        _windowRequestedMinimalSleep = sleep <= MinimalSleepRequest;
    }

    private void Reset()
    {
        _busy = TimeSpan.Zero;
        _idle = TimeSpan.Zero;
        _cycles = 0;
    }
}
