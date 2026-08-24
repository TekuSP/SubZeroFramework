namespace SubZeroFramework.Models;

/// <summary>
/// Paces a polling tier at a FIXED RATE — one tick every interval — rather than a fixed delay between ticks.
/// </summary>
/// <remarks>
/// <para>
/// The distinction is the whole point. Sleeping the full interval after each tick makes the real period
/// <c>interval + work</c>, so a tier drifts by however long its work happened to take: a 1 s secondary tier
/// that spends 500 ms waiting on an NVML call would actually run every 1.5 s, and a primary tier's cadence
/// would wander with the cost of each EC read.
/// </para>
/// <para>
/// That wander is not cosmetic for fan control. The adaptive controller differentiates temperature over time,
/// and a derivative computed against an assumed interval that the loop is not actually keeping is wrong by
/// exactly the drift — which is worst precisely when the machine is busy and the readings matter most.
/// </para>
/// <para>
/// Overruns do NOT accumulate. When a tick takes longer than the interval the next one starts immediately and
/// the schedule resets from now, rather than firing repeatedly to "catch up" — a burst of back-to-back EC
/// reads is a worse answer to being late than simply being late.
/// </para>
/// </remarks>
public static class PollingSchedule
{
    /// <summary>
    /// How long to sleep so the next tick begins one <paramref name="interval"/> after this one began.
    /// </summary>
    /// <param name="interval">The tier's configured period.</param>
    /// <param name="elapsed">How long this tick's work took.</param>
    /// <returns>
    /// The remaining time, or <see cref="TimeSpan.Zero"/> when the work already used the whole interval.
    /// </returns>
    public static TimeSpan ComputeDelay(TimeSpan interval, TimeSpan elapsed)
    {
        if (interval <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // Negative elapsed cannot happen from a monotonic clock, but a caller doing its own arithmetic could
        // hand one over; treating it as zero keeps the answer bounded by the interval either way.
        var used = elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        return used >= interval ? TimeSpan.Zero : interval - used;
    }

    /// <summary>
    /// Advances a recurring deadline to the next one strictly after <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// For work gated INSIDE another loop rather than driven by its own sleep — the secondary tier riding on
    /// the primary tick. Advancing by exactly one interval keeps the cadence anchored to the original
    /// schedule instead of drifting by the work time; clamping to <paramref name="now"/> is what stops a long
    /// stall from queueing up a run for every interval it missed.
    /// </remarks>
    /// <param name="previousDeadline">The deadline that just fired.</param>
    /// <param name="interval">The tier's configured period.</param>
    /// <param name="now">The current observation time.</param>
    public static DateTimeOffset NextDeadline(DateTimeOffset previousDeadline, TimeSpan interval, DateTimeOffset now)
    {
        if (interval <= TimeSpan.Zero)
        {
            return now;
        }

        var next = previousDeadline + interval;
        return next > now ? next : now + interval;
    }
}
