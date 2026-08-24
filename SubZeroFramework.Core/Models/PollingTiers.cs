namespace SubZeroFramework.Models;

/// <summary>
/// One sensor-polling tier and the range of intervals it will accept.
/// </summary>
public readonly record struct PollingTier(string Name, TimeSpan Default, TimeSpan Minimum, TimeSpan Maximum)
{
    /// <summary>Returns the interval unchanged when it is workable, or the nearest bound when it is not.</summary>
    public TimeSpan Clamp(TimeSpan requested)
        => requested < Minimum ? Minimum
            : requested > Maximum ? Maximum
            : requested;

    /// <summary>True when <paramref name="requested"/> would be moved by <see cref="Clamp"/>.</summary>
    public bool IsOutOfRange(TimeSpan requested) => Clamp(requested) != requested;

    /// <summary>The factory interval, as whole milliseconds — what the settings page's Default button writes.</summary>
    public long DefaultMilliseconds => checked((long)Math.Round(Default.TotalMilliseconds, MidpointRounding.AwayFromZero));
}

/// <summary>
/// The three sensor-polling tiers and their supported interval ranges.
/// </summary>
/// <remarks>
/// <para>
/// The tiers exist because the data has wildly different value-per-cost. Fan control needs temperature, fan
/// speed and CPU load as fresh as they can be had; the UI needs GPU load about as often as it redraws; and
/// installed memory, disks and the motherboard model do not change while the machine is running.
/// </para>
/// <para>
/// The bounds are here rather than inline in the provider so they can be tested without standing up a
/// provider — which needs a live EC abstraction — and so the policy is stated in one place rather than
/// implied by three separate comparisons.
/// </para>
/// <para>
/// <see cref="PollingTier.Default"/> is the single source of the factory intervals: <c>FrameworkServiceOptions</c>
/// seeds its record defaults from here and the settings page's per-field Default button writes them.
/// Change a factory interval HERE, not in several places.
/// </para>
/// <para>
/// KNOWN DISCREPANCY (2026-08-24, open decision): <c>appsettings.json</c> ships a PRIMARY interval of 2 s,
/// not the 150 ms default below, so a fresh install does not run what the Default button types. The 2 s value
/// predates the tier split and ReleasePlan P1-11 calls it "the validated EC polling rate", so it was left
/// alone rather than changed as a side effect of a settings-page feature. Note the two are not merely
/// different but inconsistent in ORDER: at 2 s the primary tier would poll SLOWER than the 1 s secondary
/// tier, which inverts the whole point of the split. Resolve deliberately, then make the two agree.
/// </para>
/// </remarks>
public static class PollingTiers
{
    /// <summary>EC telemetry and the CPU signals fan control runs on.</summary>
    /// <remarks>
    /// Floored at 50 ms because below that the EC read cannot keep up and the loop simply saturates the bus;
    /// capped at 10 s because a controller reacting to heat that old is not controlling anything.
    /// </remarks>
    public static PollingTier Primary { get; } = new("primary", TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(10));

    /// <summary>Live display data the controller does not act on, such as GPU and NPU load.</summary>
    public static PollingTier Secondary { get; } = new("secondary", TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1));

    /// <summary>Hardware inventory: installed memory, drives, motherboard, BIOS, network adapters, CPU identity.</summary>
    /// <remarks>
    /// Floored at 5 s because this tier drives Hardware.Info, whose Linux memory and drive lists each spawn a
    /// full <c>lshw</c> probe — running those in a tight loop is what caused the CPU spikes behind issue #51.
    /// </remarks>
    public static PollingTier Tertiary { get; } = new("tertiary", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), TimeSpan.FromHours(1));
}
