using System.Collections.Immutable;
using System.Linq;

namespace SubZeroFramework.Models;

public sealed record HardwareInfoCpu(
    string? Name,
    string? Caption,
    string? Description,
    string? Manufacturer,
    int Cores,
    int LogicalProcessors,
    int CurrentClockSpeedMHz,
    int MaxClockSpeedMHz,
    string? ProcessorId,
    string? SocketDesignation,
    int L1CacheSizeKb,
    int L2CacheSizeKb,
    int L3CacheSizeKb,
    bool SecondLevelAddressTranslationExtensions,
    bool VirtualizationFirmwareEnabled,
    bool VMMonitorModeExtensions,
    double? PercentProcessorTime,
    ImmutableArray<HardwareInfoCpuCore> CpuCores)
{
    public string DisplayCurrentClockSpeed => CurrentClockSpeedMHz > 0
        ? $"{CurrentClockSpeedMHz:N0} MHz"
        : "Unknown";

    public string DisplayMaxClockSpeed => MaxClockSpeedMHz > 0
        ? $"{MaxClockSpeedMHz:N0} MHz"
        : "Unknown";

    public double? EffectivePercentProcessorTime => PercentProcessorTime
        ?? (CpuCores.Length > 0
            ? CpuCores.Average(core => core.PercentProcessorTime)
            : null);
}
