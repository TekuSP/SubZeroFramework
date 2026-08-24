namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Rejects NVML readings that cannot be real.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <c>nvmlDeviceGetPowerUsage</c> returns nonsense with <c>NVML_SUCCESS</c> on a laptop
/// dGPU that is changing power state, so a status check cannot filter it. Measured on a Framework 16 with an
/// RTX 5070 whose enforced limit is 100 W: readings alternated between a believable ~17.9 W (returned in
/// 0.02 ms) and ~540 W (returned in ~600 ms — the calls that were waking the GPU), decreasing monotonically
/// call over call, which is the signature of a register being read mid-transition rather than a measurement.
/// </para>
/// <para>
/// Getting this wrong is expensive rather than cosmetic: board power is the adaptive controller's
/// feed-forward input, so a 540 W spike every other sample would command maximum fan speed indefinitely on an
/// idle machine.
/// </para>
/// <para>
/// The bound comes from the DEVICE — its own enforced power limit — rather than a constant, so it holds for a
/// 100 W laptop module and a 450 W desktop card alike.
/// </para>
/// </remarks>
public static class NvmlReadingPlausibility
{
    /// <summary>
    /// How far above its enforced limit a board is allowed to read before the value is rejected.
    /// </summary>
    /// <remarks>
    /// Not 1.0: a real board can transiently exceed its enforced limit, and clipping genuine overshoot would
    /// hide exactly the spikes feed-forward exists to catch. Two is comfortably above any real excursion and
    /// still an order of magnitude below the observed 5.4x garbage.
    /// </remarks>
    public const double LimitHeadroomFactor = 2d;

    /// <summary>
    /// Whether a power reading can be believed.
    /// </summary>
    /// <param name="watts">The reading.</param>
    /// <param name="enforcedLimitWatts">
    /// The device's enforced power limit, or null when it could not be read. With no limit there is no
    /// principled bound available, so the reading is accepted rather than filtered against a guess — a made-up
    /// ceiling would be wrong on some other card instead.
    /// </param>
    public static bool IsPlausible(double watts, double? enforcedLimitWatts)
    {
        if (double.IsNaN(watts) || double.IsInfinity(watts) || watts < 0d)
        {
            return false;
        }

        if (enforcedLimitWatts is not { } limit || limit <= 0d || double.IsNaN(limit) || double.IsInfinity(limit))
        {
            return true;
        }

        return watts <= limit * LimitHeadroomFactor;
    }

    /// <summary>
    /// How far above its reported maximum clock a GPU is allowed to read before the value is rejected.
    /// </summary>
    /// <remarks>
    /// GPUs boost, and the maximum NVML reports is not always the highest bin the hardware will actually
    /// reach. 15% is well beyond any real boost excursion and still an order below the 48% overshoot the
    /// mid-power-transition garbage produced on the reference machine.
    /// </remarks>
    public const double ClockHeadroomFactor = 1.15d;

    /// <summary>
    /// Whether a current-clock reading can be believed, given the device's maximum clock.
    /// </summary>
    /// <remarks>
    /// The same mid-transition garbage that corrupts power also reaches the clock: measured on the reference
    /// RTX 5070, <c>nvmlDeviceGetClockInfo</c> returned 4575 MHz against a stated maximum of 3090 MHz, again
    /// with <c>NVML_SUCCESS</c>.
    ///
    /// Headroom is allowed, because GPUs boost. An earlier version compared strictly on the reasoning that a
    /// maximum clock is a hardware ceiling rather than a policy cap — that was asserted, not measured, and it
    /// would silently discard every reading from a GPU that legitimately boosts past the figure
    /// <c>nvmlDeviceGetMaxClockInfo</c> reports. The headroom is smaller than the power rule's because a clock
    /// maximum is still a much harder bound than an enforced power limit, and it does not need to be large:
    /// the observed garbage was 1.48x the maximum, so <see cref="ClockHeadroomFactor"/> rejects it with room
    /// to spare while leaving any real boost intact.
    /// </remarks>
    public static bool IsClockPlausible(double megahertz, double? maximumMegahertz)
    {
        if (double.IsNaN(megahertz) || double.IsInfinity(megahertz) || megahertz < 0d)
        {
            return false;
        }

        if (maximumMegahertz is not { } maximum || maximum <= 0d || double.IsNaN(maximum) || double.IsInfinity(maximum))
        {
            return true;
        }

        return megahertz <= maximum * ClockHeadroomFactor;
    }
}
