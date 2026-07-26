using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoInventorySnapshot
{
    public HardwareInfoOperatingSystem? OperatingSystem { get; init; }

    public HardwareInfoComputerSystem? ComputerSystem { get; init; }

    public ImmutableArray<HardwareInfoMemoryModule> MemoryModules { get; init; } = ImmutableArray<HardwareInfoMemoryModule>.Empty;

    public ImmutableArray<HardwareInfoDrive> Drives { get; init; } = ImmutableArray<HardwareInfoDrive>.Empty;

    public ImmutableArray<HardwareInfoNetworkAdapter> NetworkAdapters { get; init; } = ImmutableArray<HardwareInfoNetworkAdapter>.Empty;

    /// <summary>Neural processors and other compute accelerators; static identity, not their live load.</summary>
    public ImmutableArray<HardwareInfoComputeAccelerator> ComputeAccelerators { get; init; } = ImmutableArray<HardwareInfoComputeAccelerator>.Empty;

    public HardwareInfoMotherboard? Motherboard { get; init; }

    public HardwareInfoBios? Bios { get; init; }
}
