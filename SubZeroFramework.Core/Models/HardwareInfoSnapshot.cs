using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoSnapshot
{
    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    public string? LastError { get; init; }

    public HardwareInfoInventorySnapshot Inventory { get; init; } = new();

    public HardwareInfoRuntimeSnapshot Runtime { get; init; } = new();

    /// <summary>
    /// Firmware versions for the peripherals, PD controllers and drives.
    /// </summary>
    /// <remarks>
    /// Rides on this snapshot rather than an RPC of its own because Device Capabilities already consumes it
    /// and nothing here changes at runtime — a firmware update needs a restart. The provider collects it once
    /// and reuses the result, so carrying it on a repeatedly-rebuilt snapshot costs one collection, not one
    /// per rebuild.
    /// </remarks>
    public FirmwareInventorySnapshot Firmware { get; init; } = FirmwareInventorySnapshot.Empty;

    public HardwareInfoOperatingSystem? OperatingSystem => Inventory.OperatingSystem;

    public HardwareInfoComputerSystem? ComputerSystem => Inventory.ComputerSystem;

    public ImmutableArray<HardwareInfoCpu> Cpus => Runtime.Cpus;

    public ImmutableArray<HardwareInfoMemoryModule> MemoryModules => Inventory.MemoryModules;

    public ImmutableArray<HardwareInfoDrive> Drives => Inventory.Drives;

    public ImmutableArray<HardwareInfoNetworkAdapter> NetworkAdapters => Inventory.NetworkAdapters;

    public ImmutableArray<HardwareInfoComputeAccelerator> ComputeAccelerators => Inventory.ComputeAccelerators;

    public HardwareInfoMemoryStatus? MemoryStatus => Runtime.MemoryStatus;

    public ImmutableArray<HardwareInfoMonitor> Monitors => Runtime.Monitors;

    public HardwareInfoMotherboard? Motherboard => Inventory.Motherboard;

    public HardwareInfoBios? Bios => Inventory.Bios;

    public ImmutableArray<HardwareInfoVideoController> VideoControllers => Runtime.VideoControllers;
}
