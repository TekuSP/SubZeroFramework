namespace SubZeroFramework.Services.Control;

/// <summary>
/// Turns the Windows <c>Processor Information</c> counters into the busy fraction a human means by "CPU usage".
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="WindowsPdhControlTelemetryReader"/>, and platform-neutral, because it is
/// arithmetic rather than interop. The reader that uses it exists only in the windows TFM and needs live PDH
/// counters to run at all, so leaving this inside it would have put the one part that can be checked against
/// known numbers behind the one part that cannot.
/// </para>
/// </remarks>
public static class ProcessorUtilityMath
{
    /// <summary>
    /// Below this, a speed ratio is treated as unusable rather than divided by.
    /// </summary>
    /// <remarks>
    /// A parked or deeply idled core can report a ratio at or near zero. Dividing by it would turn a core
    /// doing nothing into one reported as fully busy — the same failure as the clamp, in the other direction
    /// and far louder.
    /// </remarks>
    public const double MinimumUsableRatio = 0.01d;

    /// <summary>
    /// Cancels the clock out of a utility reading, leaving busy time.
    /// </summary>
    /// <param name="utility">
    /// <c>% Processor Utility</c> as a fraction — work done against what the processor could do at NOMINAL
    /// speed. Over 1 while boosting.
    /// </param>
    /// <param name="performanceRatio">
    /// <c>% Processor Performance</c> as a fraction — the speed multiple that <paramref name="utility"/> is
    /// scaled by. Null or implausibly small leaves the reading unscaled.
    /// </param>
    /// <returns>The busy fraction, 0–1.</returns>
    /// <remarks>
    /// <para>
    /// Utility carries the clock inside it: a core 60% busy while boosting to 2.03x nominal reports about
    /// 122%. Dividing by the performance ratio — the same speed multiple — cancels it. Verified against
    /// <c>% Processor Time</c> on a Ryzen AI 9 HX 370: 19.3% against 19.5% idle, 37.7% against 36.8% at six
    /// busy threads, 83.1% against 84.1% at twelve.
    /// </para>
    /// <para>
    /// The clamp lands AFTER the division, and that ordering is the whole point. Clamping the raw utility
    /// instead — which is what this replaced — pinned the result at exactly 1.0 for any load above roughly
    /// half on a machine that boosts to 2x, and reported 40% on an idle machine that was 19% busy. The
    /// reported figure stopped moving well before the processor was actually busy.
    /// </para>
    /// </remarks>
    public static double ToBusyFraction(double utility, double? performanceRatio)
    {
        if (!double.IsFinite(utility))
        {
            return 0d;
        }

        var busy = performanceRatio is { } ratio && double.IsFinite(ratio) && ratio > MinimumUsableRatio
            ? utility / ratio
            : utility;

        return Math.Clamp(busy, 0d, 1d);
    }
}
