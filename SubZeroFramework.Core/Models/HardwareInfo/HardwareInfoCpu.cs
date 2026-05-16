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
    double PercentProcessorTime);
