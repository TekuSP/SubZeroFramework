namespace SubZeroFramework.Models;

/// <summary>What kind of processor a compute device is. Decides which telemetry entity kind it publishes as.</summary>
public enum ComputeDeviceKind
{
    Gpu,
    Npu,
}

/// <summary>
/// Why a compute device is running below its rated speed.
/// </summary>
/// <remarks>
/// Vendor-neutral, but modelled on NVML's <c>nvmlClocksThrottleReasons</c> because that is the only source
/// that reports throttling outright rather than leaving it to be inferred from a clock ratio.
///
/// A NULL <see cref="ComputeDeviceUtilization.ThrottleReasons"/> means the device does not report this at all;
/// <see cref="None"/> means it does report it and is not throttling. The adaptive fan controller escalates on
/// the second and cannot act on the first, so collapsing them would turn "we cannot tell" into "everything is
/// fine" — which is exactly the case that ends in a thermal stall.
/// </remarks>
[Flags]
public enum ComputeThrottleReasons
{
    /// <summary>Reported, and running at its rated speed.</summary>
    None = 0,

    /// <summary>Held back by the power budget.</summary>
    PowerLimit = 1 << 0,

    /// <summary>Held back by temperature — the reason that matters most here, and the one more airflow fixes.</summary>
    ThermalLimit = 1 << 1,

    /// <summary>Held back by an externally applied limit, such as a user or driver clock cap.</summary>
    ApplicationLimit = 1 << 2,

    /// <summary>Idling down because there is no work, which is not a problem to solve.</summary>
    Idle = 1 << 3,

    /// <summary>Throttled for a reason the source named but this model does not distinguish.</summary>
    Other = 1 << 4,
}

/// <summary>
/// One GPU or NPU's load at a point in time.
/// </summary>
/// <remarks>
/// <see cref="UtilizationPercent"/> is busy-TIME share, not capacity — see
/// <see cref="TelemetryMetric.UtilizationPercent"/>. Devices are reported individually and never blended: a 4%
/// integrated GPU and a 97% discrete one do not average into anything a user can act on.
/// </remarks>
public sealed record ComputeDeviceUtilization
{
    /// <summary>
    /// Stable, restart-safe identity for the device — the PCI address on Linux, the device instance path on
    /// Windows. Telemetry channels key off this, NOT the enumeration order and NOT a Windows adapter LUID
    /// (LUIDs are regenerated on reboot, so they identify a device only within one session).
    /// </summary>
    public required string DeviceKey { get; init; }

    public required ComputeDeviceKind Kind { get; init; }

    /// <summary>Human-readable name, e.g. "AMD Radeon(TM) 890M Graphics". Falls back to a generic label.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Busy-time share over the sampling window, 0–100.</summary>
    public required double UtilizationPercent { get; init; }

    // Everything below is OPTIONAL and was added for adaptive fan control. Existing consumers that only read
    // UtilizationPercent are unaffected, and a reader that cannot supply a field leaves it null rather than
    // reporting a zero that would read as a genuine measurement.

    /// <summary>
    /// Board power draw. The single most useful signal here: it leads temperature, so a controller can start
    /// moving air for heat that has been generated but has not yet reached a sensor.
    /// </summary>
    public double? PowerWatts { get; init; }

    /// <summary>Primary die temperature — the "edge" sensor on amdgpu.</summary>
    public double? TemperatureCelsius { get; init; }

    /// <summary>
    /// Hottest measured point on the die — "junction" on amdgpu. Runs well above the edge sensor and is
    /// usually what actually trips a thermal limit, so it is kept separate rather than blended into one number.
    /// </summary>
    public double? HotspotTemperatureCelsius { get; init; }

    /// <summary>Current core clock.</summary>
    /// <remarks>
    /// The weakest of these signals: a low clock is only a PROXY for throttling, and an idle device shows one
    /// too. Where <see cref="ThrottleReasons"/> is available, prefer it. Only meaningful against
    /// <see cref="MaxCoreClockMegahertz"/> — an absolute megahertz figure says nothing without knowing what
    /// this part is capable of.
    /// </remarks>
    public double? CoreClockMegahertz { get; init; }

    /// <summary>
    /// The highest clock the driver will currently allow, which is what <see cref="CoreClockMegahertz"/> is
    /// measured against.
    /// </summary>
    /// <remarks>
    /// The POLICY cap rather than the silicon maximum, so the ratio answers "is this device reaching the
    /// speed it is permitted" — which is the question a throttle proxy is trying to ask.
    /// </remarks>
    public double? MaxCoreClockMegahertz { get; init; }

    /// <summary>
    /// Current clock as a fraction of the permitted maximum, or null when either half is unavailable.
    /// </summary>
    /// <remarks>
    /// Sustained values well below 1 suggest throttling — but ONLY alongside meaningful
    /// <see cref="UtilizationPercent"/>, because an idle device clocks down too and looks identical. Where
    /// <see cref="ThrottleReasons"/> exists it answers this outright and should be used instead.
    /// </remarks>
    public double? CoreClockRatio
        => CoreClockMegahertz is { } current && MaxCoreClockMegahertz is { } maximum && maximum > 0d
            ? current / maximum
            : null;

    /// <summary>
    /// Why the device is running below its rated speed, or null when it does not report this.
    /// </summary>
    /// <remarks>
    /// Null and <see cref="ComputeThrottleReasons.None"/> are NOT interchangeable — see the enum's remarks.
    /// </remarks>
    public ComputeThrottleReasons? ThrottleReasons { get; init; }


    /// <summary>
    /// Fills this sample's missing optional fields from <paramref name="other"/>, which describes the same
    /// device seen through a different source.
    /// </summary>
    /// <remarks>
    /// Exists because a device can legitimately be visible to two readers that know different things about
    /// it. On Windows the PDH counter set reports utilisation for every adapter regardless of vendor, while
    /// NVML reports power, temperature and throttle reasons for the NVIDIA one — neither is a superset of the
    /// other, so picking a winner would throw away real data.
    ///
    /// Only NULL fields are filled: whichever source answered first keeps its reading, so this can never
    /// silently overwrite a measurement with a second opinion. Utilisation and identity are left alone
    /// entirely, since those come from the source that owns the device key.
    /// </remarks>
    public ComputeDeviceUtilization EnrichFrom(ComputeDeviceUtilization other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return this with
        {
            PowerWatts = PowerWatts ?? other.PowerWatts,
            TemperatureCelsius = TemperatureCelsius ?? other.TemperatureCelsius,
            HotspotTemperatureCelsius = HotspotTemperatureCelsius ?? other.HotspotTemperatureCelsius,
            CoreClockMegahertz = CoreClockMegahertz ?? other.CoreClockMegahertz,
            MaxCoreClockMegahertz = MaxCoreClockMegahertz ?? other.MaxCoreClockMegahertz,
            VramUsedBytes = VramUsedBytes ?? other.VramUsedBytes,
            VramTotalBytes = VramTotalBytes ?? other.VramTotalBytes,
            ThrottleReasons = ThrottleReasons ?? other.ThrottleReasons,
        };
    }


    /// <summary>Video memory currently in use, in bytes.</summary>
    /// <remarks>
    /// Deliberately memory USED rather than memory-bandwidth utilisation. NVML reports both — its
    /// <c>nvmlUtilization_t.memory</c> field is the share of time the memory bus was busy — but ADLX reports
    /// only usage, and a figure that means two different things per vendor is worse than one that means the
    /// same thing everywhere. On an integrated GPU this is a carve-out of system memory, not dedicated VRAM.
    /// </remarks>
    public double? VramUsedBytes { get; init; }

    /// <summary>Total video memory, in bytes — the denominator for <see cref="VramUtilizationPercent"/>.</summary>
    public double? VramTotalBytes { get; init; }

    /// <summary>Video memory in use as a percentage of the total, or null when either end is unknown.</summary>
    public double? VramUtilizationPercent
        => VramUsedBytes is { } used && VramTotalBytes is { } total && total > 0d
            ? Math.Clamp(used / total * 100d, 0d, 100d)
            : null;

    /// <summary>True when any signal beyond utilisation was read, so a consumer can tell a rich source from a bare one.</summary>
    public bool HasExtendedTelemetry
        => PowerWatts is not null
            || TemperatureCelsius is not null
            || HotspotTemperatureCelsius is not null
            || CoreClockMegahertz is not null
            || MaxCoreClockMegahertz is not null
            || ThrottleReasons is not null;
}

/// <summary>
/// Reads how busy each GPU / NPU in the machine is. One implementation per platform; every implementation is
/// allowed to report nothing.
/// </summary>
/// <remarks>
/// EVERY source behind this interface is optional. A machine without the NVIDIA driver, without Intel's
/// compute runtime, or on a kernel too old for a given accelerator must yield an empty (or shorter) list —
/// never an exception out of a telemetry tick. Probe with <see cref="IsAvailable"/>, and treat a throwing or
/// missing source as "this device is not reportable", which the telemetry layer turns into an unavailable
/// channel and the UI simply omits.
/// </remarks>
public interface IComputeUtilizationReader : IDisposable
{
    /// <summary>False when this platform/machine cannot report anything; <see cref="Sample"/> then returns empty.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Current utilization for every readable device. Called on the fast telemetry tier (~1 s), so it must be
    /// cheap and must not block: no subprocesses, no reopening of handles, no I/O that can hang.
    /// </summary>
    IReadOnlyList<ComputeDeviceUtilization> Sample();
}
