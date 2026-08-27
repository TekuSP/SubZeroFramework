namespace SubZeroFramework.Services.Compute;

/// <summary>
/// The delta arithmetic for IGCL's monotonic counters, separated from the interop so it can be tested on any
/// machine — the same split RAPL's wraparound math got.
/// </summary>
/// <remarks>
/// IGCL reports ENERGY (joules) and BUSY TIME (seconds) as monotonic counters stamped with the snapshot's own
/// timestamp. Average power and average utilisation are the deltas between two snapshots divided by the
/// elapsed time on that same clock — using the snapshot clock rather than a local stopwatch, because the
/// counters and the timestamp advance together and a mixed-clock division would skew every reading by the
/// sampling jitter.
/// </remarks>
public static class IgclCounterMath
{
    /// <summary>
    /// Below this the elapsed window is too short for the counter's ~1 ms accuracy to produce a meaningful
    /// average, and division would amplify quantisation into wild readings.
    /// </summary>
    public const double MinimumWindowSeconds = 0.1d;

    /// <summary>
    /// Average power in watts across two energy-counter snapshots, or null when the window is unusable.
    /// </summary>
    /// <remarks>
    /// A NEGATIVE delta — the counter or the clock going backwards — invalidates the window rather than
    /// clamping to zero: it means the counter reset (driver reload, suspend cycle), and the only honest
    /// answer for that window is "unknown".
    /// </remarks>
    public static double? AveragePowerWatts(
        double? previousJoules, double? previousSeconds,
        double? currentJoules, double? currentSeconds)
    {
        if (previousJoules is not { } j0 || previousSeconds is not { } t0
            || currentJoules is not { } j1 || currentSeconds is not { } t1)
        {
            return null;
        }

        var elapsed = t1 - t0;
        if (elapsed < MinimumWindowSeconds)
        {
            return null;
        }

        var joules = j1 - j0;
        return joules < 0d ? null : joules / elapsed;
    }

    /// <summary>
    /// Average utilisation percent across two busy-time snapshots, or null when the window is unusable.
    /// </summary>
    /// <remarks>
    /// Clamped to 100: busy seconds can slightly exceed wall seconds through counter granularity, and 103%
    /// busy is a rounding artefact, not a measurement.
    /// </remarks>
    public static double? AverageActivityPercent(
        double? previousBusySeconds, double? previousSeconds,
        double? currentBusySeconds, double? currentSeconds)
    {
        if (previousBusySeconds is not { } b0 || previousSeconds is not { } t0
            || currentBusySeconds is not { } b1 || currentSeconds is not { } t1)
        {
            return null;
        }

        var elapsed = t1 - t0;
        if (elapsed < MinimumWindowSeconds)
        {
            return null;
        }

        var busy = b1 - b0;
        return busy < 0d ? null : Math.Clamp(busy / elapsed * 100d, 0d, 100d);
    }
}
