using System.Collections.Immutable;

namespace SubZeroFramework.Models;

/// <summary>
/// The CPU-side signals the adaptive fan controller runs on, read once per primary-tier tick.
/// </summary>
/// <remarks>
/// Every field is nullable because availability is per-signal, not per-machine: package power comes from a
/// RAPL energy meter, which a Windows machine reads through PDH and a Linux machine through powercap sysfs,
/// and either can be missing while utilisation and the performance ratio are both fine. A consumer must
/// degrade per signal rather than treating the whole sample as all-or-nothing.
/// </remarks>
public sealed record ControlTelemetrySample
{
    /// <summary>A sample carrying nothing — what a reader returns when it cannot read this machine.</summary>
    public static ControlTelemetrySample Unavailable { get; } = new();

    /// <summary>Aggregate CPU busy share over the interval since the previous sample, 0–1.</summary>
    public double? CpuUtilizationFraction { get; init; }

    /// <summary>Per-logical-processor busy share over the same interval, in stable ordinal order.</summary>
    public ImmutableArray<double> PerCoreUtilizationFraction { get; init; } = [];

    /// <summary>
    /// Current clock as a fraction of base clock. The FALLBACK throttle signal, used only where the embedded
    /// controller does not report <see cref="EcThrottle"/>: sustained values below 1 mean the processor is
    /// not reaching its rated speed. Values above 1 are normal and mean turbo, so a consumer must not clamp
    /// this to 1 and conclude everything is fine.
    /// </summary>
    public double? CpuPerformanceRatio { get; init; }

    /// <summary>
    /// What the embedded controller says about throttling, or null where it does not report it.
    /// </summary>
    /// <remarks>
    /// Authoritative, and preferred over <see cref="CpuPerformanceRatio"/> wherever it is present. The ratio
    /// is a proxy that also falls for power limits, parked cores and a workload that merely went idle — none
    /// of which is a thermal emergency, and each of which used to spin the fan up for no reason a user could
    /// explain.
    /// </remarks>
    public EcThrottleSeverity? EcThrottle { get; init; }

    /// <summary>
    /// The clock the processor is actually running at, in megahertz.
    /// </summary>
    /// <remarks>
    /// Carried on the CONTROL sample so the displayed clock moves with the machine. The inventory tier also
    /// reports a current clock, but it sweeps every thirty seconds by default and its source measures nothing
    /// — a "current clock" that changes twice a minute reads as broken next to a live power figure.
    /// </remarks>
    public double? CpuClockMegahertz { get; init; }

    /// <summary>
    /// CPU package power, in watts.
    /// </summary>
    /// <remarks>
    /// Available on both platforms from the processor's own RAPL package domain: powercap sysfs on Linux, the
    /// Energy Meter counter set on Windows. Neither needs a kernel driver. Null where the firmware exposes no
    /// energy meter, in which case the controller substitutes system power.
    /// </remarks>
    public double? CpuPackagePowerWatts { get; init; }

    /// <summary>
    /// GPU package power, in watts, when a vendor reader can supply it.
    /// </summary>
    /// <remarks>
    /// A second heat source into the same chassis, and on a Framework 16 with the graphics module it can
    /// exceed the CPU. Folded into <see cref="ThermalLoadWatts"/> alongside package power rather than tracked
    /// separately, because the fan does not care which die the heat came from.
    /// </remarks>
    public double? GpuPowerWatts { get; init; }

    /// <summary>
    /// The fastest graphics core clock reported, in MHz.
    /// </summary>
    /// <remarks>
    /// Not used for control — it is the GPU's answer to <see cref="CpuPerformanceRatio"/>, recorded during a
    /// calibration so the run can say what more fan actually BOUGHT. Cooling that produces no extra sustained
    /// clock is cooling nobody needed, and that is only knowable by measuring speed alongside temperature.
    /// </remarks>
    public double? GpuCoreClockMegahertz { get; init; }

    /// <summary>
    /// The busiest graphics device's busy share, 0–1.
    /// </summary>
    /// <remarks>
    /// Also not used for control; it exists so a GPU-load calibration can show the load actually TOOK, the
    /// way <see cref="CpuUtilizationFraction"/> does for a CPU-load one. The busiest device rather than a
    /// sum, because busy shares do not add and the loaded GPU is by definition the busiest one.
    /// </remarks>
    public double? GpuUtilizationFraction { get; init; }

    /// <summary>
    /// Total system draw, in watts, derived from the charger less battery charging.
    /// </summary>
    /// <remarks>
    /// The Windows answer. It is coarser than component power — it carries the display and everything else —
    /// but it moves with CPU and GPU activity, which is what feed-forward needs. Absent on battery, where
    /// there is no adapter to measure.
    /// </remarks>
    public double? SystemPowerWatts { get; init; }

    // How these three combine into the figure feed-forward acts on is deliberately NOT decided here. A sample
    // knows only what it managed to read this tick, and choosing per tick is exactly what makes the
    // composition flap as a discrete GPU enters and leaves low-power states. That decision belongs to
    // ThermalLoadPolicy, which makes it once for the machine and then holds it.

    /// <summary>True when at least one signal was read. A sample with nothing in it must not look like idle.</summary>
    public bool HasAnyReading
        => CpuUtilizationFraction is not null
            || CpuPerformanceRatio is not null
            || CpuPackagePowerWatts is not null
            || GpuPowerWatts is not null
            || SystemPowerWatts is not null
            || !PerCoreUtilizationFraction.IsDefaultOrEmpty;

}

/// <summary>
/// A <see cref="ControlTelemetrySample"/> together with when it was taken.
/// </summary>
/// <remarks>
/// The timestamp is not part of the sample itself because a reader produces values, not history. It matters
/// once the value is CACHED and handed to a consumer on a different schedule: a stopped polling loop would
/// otherwise leave the last reading in place indefinitely, and a consumer acting on it — the fan controller —
/// would keep boosting on a utilisation figure that stopped being true minutes ago. Consumers compare this
/// against now and treat anything old as no reading at all.
/// </remarks>
public sealed record ObservedControlTelemetry(ControlTelemetrySample Sample, DateTimeOffset ObservedAt)
{
    /// <summary>No reading has been taken. Dated to <see cref="DateTimeOffset.MinValue"/>, so any staleness check rejects it.</summary>
    public static ObservedControlTelemetry None { get; } = new(ControlTelemetrySample.Unavailable, DateTimeOffset.MinValue);
}

/// <summary>
/// Reads the CPU signals that drive fan control, cheaply enough to run on the primary tier.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the previous source did not qualify. CPU usage used to arrive through
/// <c>Hardware.Info</c>'s <c>RefreshCPUList(true, 500, true)</c>, where the <c>500</c> is a blocking
/// half-second sleep between two measurements, on top of WMI — a measured ~600 ms out of a 1 s budget, for
/// the controller's only anticipatory input.
/// </para>
/// <para>
/// Every implementation reads CUMULATIVE counters and differentiates against its own previous sample, so a
/// tick costs a read and some arithmetic and never sleeps. The first <see cref="Sample"/> after construction
/// has no predecessor to difference against and therefore reports no utilisation; that is expected, not a
/// failure.
/// </para>
/// <para>
/// Like <see cref="IComputeUtilizationReader"/>, every source here is optional and none may throw out of a
/// telemetry tick: an unreadable counter degrades to a null field, and an unusable platform degrades to
/// <see cref="ControlTelemetrySample.Unavailable"/>.
/// </para>
/// </remarks>
public interface IControlTelemetryReader : IDisposable
{
    /// <summary>False when this machine cannot report anything; <see cref="Sample"/> then returns nothing.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Reads every available signal. Runs on the primary tier, so it must be cheap and must not block: no
    /// subprocesses, no sleeps, no reopening of handles, no I/O that can hang.
    /// </summary>
    ControlTelemetrySample Sample();
}
