using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoInventorySnapshot
{
    public HardwareInfoOperatingSystem? OperatingSystem { get; init; }

    public HardwareInfoComputerSystem? ComputerSystem { get; init; }

    public ImmutableArray<HardwareInfoMemoryModule> MemoryModules { get; init; } = ImmutableArray<HardwareInfoMemoryModule>.Empty;

    public HardwareInfoMotherboard? Motherboard { get; init; }

    public HardwareInfoBios? Bios { get; init; }
}
