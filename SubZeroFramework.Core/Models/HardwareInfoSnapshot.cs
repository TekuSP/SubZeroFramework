using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoSnapshot
{
    public DateTimeOffset ObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    public string? LastError { get; init; }

    public HardwareInfoOperatingSystem? OperatingSystem { get; init; }

    public HardwareInfoComputerSystem? ComputerSystem { get; init; }

    public ImmutableArray<HardwareInfoCpu> Cpus { get; init; } = ImmutableArray<HardwareInfoCpu>.Empty;

    public ImmutableArray<HardwareInfoMemoryModule> MemoryModules { get; init; } = ImmutableArray<HardwareInfoMemoryModule>.Empty;

    public HardwareInfoMemoryStatus? MemoryStatus { get; init; }

    public HardwareInfoMotherboard? Motherboard { get; init; }

    public HardwareInfoBios? Bios { get; init; }

    public ImmutableArray<HardwareInfoVideoController> VideoControllers { get; init; } = ImmutableArray<HardwareInfoVideoController>.Empty;
}
