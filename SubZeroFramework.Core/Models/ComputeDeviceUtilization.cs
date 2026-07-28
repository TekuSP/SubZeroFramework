namespace SubZeroFramework.Models;

/// <summary>What kind of processor a compute device is. Decides which telemetry entity kind it publishes as.</summary>
public enum ComputeDeviceKind
{
    Gpu,
    Npu,
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
