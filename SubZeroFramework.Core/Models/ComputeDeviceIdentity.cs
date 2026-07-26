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
