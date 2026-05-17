using System.Collections.Immutable;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoRuntimeSnapshot
{
    public ImmutableArray<HardwareInfoCpu> Cpus { get; init; } = ImmutableArray<HardwareInfoCpu>.Empty;

    public ImmutableArray<HardwareInfoMonitor> Monitors { get; init; } = ImmutableArray<HardwareInfoMonitor>.Empty;

    public HardwareInfoMemoryStatus? MemoryStatus { get; init; }

    public ImmutableArray<HardwareInfoVideoController> VideoControllers { get; init; } = ImmutableArray<HardwareInfoVideoController>.Empty;
}
