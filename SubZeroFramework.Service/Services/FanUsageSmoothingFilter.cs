namespace SubZeroFramework.Service.Services;

/// <summary>
/// Fast-attack / slow-decay smoothing for a usage fraction (0..1). A rising sample is taken instantly —
/// the whole point of the usage modifier is to spin fans up before heat appears — while a falling sample
/// only decays the held value exponentially (half-life <see cref="DecayHalfLife"/>), so one-second load
/// spikes ramp the fans without making them surge and drop every sample. A missing sample also decays,
/// so a stalled usage source fades the boost out instead of freezing it.
/// </summary>
public sealed class FanUsageSmoothingFilter(TimeSpan decayHalfLife)
{
    public TimeSpan DecayHalfLife { get; } = decayHalfLife > TimeSpan.Zero
        ? decayHalfLife
        : throw new ArgumentOutOfRangeException(nameof(decayHalfLife), "The decay half-life must be positive.");

    /// <summary>The current smoothed usage fraction, or null before the first valid sample.</summary>
    public double? Current { get; private set; }

    /// <summary>Feeds one sample taken <paramref name="elapsed"/> after the previous one and returns the smoothed value.</summary>
    public double? Sample(double? rawFraction, TimeSpan elapsed)
    {
        if (Current is double previous && elapsed > TimeSpan.Zero)
        {
            Current = previous * Math.Pow(0.5d, elapsed.TotalSeconds / DecayHalfLife.TotalSeconds);
        }

        if (rawFraction is double raw && !double.IsNaN(raw))
        {
            var clamped = Math.Clamp(raw, 0d, 1d);
            Current = Current is double decayed ? Math.Max(clamped, decayed) : clamped;
        }

        return Current;
    }
}
