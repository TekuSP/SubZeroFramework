namespace SubZeroFramework.Models;

public sealed record HardwareInfoMemoryStatus(
    ulong TotalPhysical,
    ulong AvailablePhysical,
    ulong TotalPageFile,
    ulong AvailablePageFile,
    ulong TotalVirtual,
    ulong AvailableVirtual,
    ulong AvailableExtendedVirtual);
