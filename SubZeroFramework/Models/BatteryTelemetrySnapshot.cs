using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record BatteryTelemetrySnapshot
{
    public required int BatteryIndex { get; init; }

    public required string DisplayName { get; init; }
    
    public DateTimeOffset ObservedAt { get; init; }

    public double? ChargePercent { get; init; }
    
    public double? Voltage { get; init; }
    
    public double? Amperage { get; init; }

    public FrameworkPowerSourceState? PowerSourceState { get; init; }

    public FrameworkBatteryState? BatteryState { get; init; }

    public string? Manufacturer { get; init; }

    public string? ModelNumber { get; init; }

    public string? SerialNumber { get; init; }

    public string? BatteryType { get; init; }

    public double? RemainingCapacityAmpereHours { get; init; }

    public double? DesignCapacityAmpereHours { get; init; }

    public double? LastFullChargeCapacityAmpereHours { get; init; }

    public double? DesignVoltageVolts { get; init; }

    public uint? CycleCount { get; init; }

    public bool IsAvailable { get; init; }
}
