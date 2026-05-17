using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoSnapshot
{
    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    public string? LastError { get; init; }

    public HardwareInfoInventorySnapshot Inventory { get; init; } = new();

    public HardwareInfoRuntimeSnapshot Runtime { get; init; } = new();

    public HardwareInfoOperatingSystem? OperatingSystem => Inventory.OperatingSystem;

    public HardwareInfoComputerSystem? ComputerSystem => Inventory.ComputerSystem;

    public ImmutableArray<HardwareInfoCpu> Cpus => Runtime.Cpus;

    public ImmutableArray<HardwareInfoMemoryModule> MemoryModules => Inventory.MemoryModules;

    public ImmutableArray<HardwareInfoDrive> Drives => Inventory.Drives;

    public ImmutableArray<HardwareInfoNetworkAdapter> NetworkAdapters => Inventory.NetworkAdapters;

    public HardwareInfoMemoryStatus? MemoryStatus => Runtime.MemoryStatus;

    public ImmutableArray<HardwareInfoMonitor> Monitors => Runtime.Monitors;

    public HardwareInfoMotherboard? Motherboard => Inventory.Motherboard;

    public HardwareInfoBios? Bios => Inventory.Bios;

    public ImmutableArray<HardwareInfoVideoController> VideoControllers => Runtime.VideoControllers;
}
