namespace SubZeroFramework.Models;

public sealed record HardwareInfoMemoryModule(
    string? BankLabel,
    ulong CapacityBytes,
    uint DataWidth,
    string? MemoryType,
    string? FormFactor,
    uint SpeedMHz,
    uint MaxVoltage,
    uint MinVoltage,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber);
