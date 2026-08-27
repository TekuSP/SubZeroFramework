namespace SubZeroFramework.Models;

/// <summary>
/// A compute device as the OS describes it, independent of any performance counter.
/// </summary>
public sealed record ComputeDeviceIdentity
{
    /// <summary>Stable identity — see <see cref="ComputeDeviceUtilization.DeviceKey"/>.</summary>
    public required string DeviceKey { get; init; }

    public required ComputeDeviceKind Kind { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Chip or board vendor, e.g. "Advanced Micro Devices, Inc." — null when unknown.</summary>
    public string? Vendor { get; init; }

    /// <summary>Longer OS-provided description, when it says something the name does not.</summary>
    public string? Description { get; init; }

    /// <summary>Kernel module or Windows driver bound to the device, e.g. "amdxdna", "intel_vpu".</summary>
    public string? DriverName { get; init; }

    public string? DriverVersion { get; init; }

    /// <summary>Device firmware version, where the platform exposes one separately from the driver.</summary>
    public string? FirmwareVersion { get; init; }

    /// <summary>Where the device sits, in whatever form the platform describes it.</summary>
    /// <remarks>
    /// Human-readable and platform-shaped: Linux gives the PCI slot name, Windows gives text such as
    /// "PCI bus 194, device 0, function 0". Do NOT match on this — use <see cref="PciAddress"/>.
    /// </remarks>
    public string? Location { get; init; }

    /// <summary>
    /// Canonical lower-case PCI address, e.g. <c>0000:c2:00.0</c>, or null for a device that has none.
    /// </summary>
    /// <remarks>
    /// Exists to JOIN this device to a source that identifies devices by bus address rather than by OS
    /// identity — NVML being the case that forced it. On Windows it is derived from the numeric
    /// <c>DEVPKEY_Device_BusNumber</c> and <c>DEVPKEY_Device_Address</c> rather than parsed out of
    /// <see cref="Location"/>, whose wording is localized and therefore unmatchable.
    /// </remarks>
    public string? PciAddress { get; init; }

    /// <summary>
    /// Windows adapter LUID, when the platform has one. SESSION-SCOPED: Windows regenerates LUIDs across
    /// reboots, so this correlates a device to its performance-counter instances within one run and must
    /// never be persisted or used as <see cref="DeviceKey"/>.
    /// </summary>
    public long? AdapterLuid { get; init; }

    /// <summary>Windows physical adapter index that pairs with <see cref="AdapterLuid"/> in counter names.</summary>
    public int? PhysicalAdapterIndex { get; init; }
}

/// <summary>
/// Enumerates the machine's GPUs and NPUs and names them. Runs on the SLOW telemetry tier — the device set is
/// near-static, and the Windows implementation costs hundreds of milliseconds, so it must never be called per
/// sample.
/// </summary>
/// <remarks>
/// Like <see cref="IComputeUtilizationReader"/>, this is allowed to report nothing. When identity cannot be
/// resolved the utilization reader still publishes the device under a generic name rather than dropping it —
/// a nameless readout beats a missing one.
/// </remarks>
public interface IComputeDeviceIdentityResolver
{
    /// <summary>Every GPU/NPU the OS reports. Empty when the platform cannot enumerate them.</summary>
    IReadOnlyList<ComputeDeviceIdentity> Enumerate();
}
