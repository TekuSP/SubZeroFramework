using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record CurrentTelemetryValue
{
    public required TelemetryChannelId ChannelId { get; init; }

    public required string DisplayName { get; init; }

    public string? UnitSymbol { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public double? NumericValue { get; init; }

    public FrameworkTemperatureState? TemperatureState { get; init; }

    /// <summary>Platform role of a temperature sensor (thermal channels only); null for non-thermal channels.</summary>
    public FrameworkSensorName? SensorName { get; init; }

    /// <summary>Platform role of a fan (fan channels only); null for non-fan channels.</summary>
    public FrameworkFanName? FanName { get; init; }

    public FrameworkPowerSourceState? PowerSourceState { get; init; }

    public FrameworkBatteryState? BatteryState { get; init; }

    public string? BatteryManufacturer { get; init; }

    public string? BatteryModelNumber { get; init; }

    public string? BatterySerialNumber { get; init; }

    public string? BatteryType { get; init; }

    public double? BatteryRemainingCapacityAmpereHours { get; init; }

    public double? BatteryDesignCapacityAmpereHours { get; init; }

    public double? BatteryLastFullChargeCapacityAmpereHours { get; init; }

    public double? BatteryDesignVoltageVolts { get; init; }

    public uint? BatteryCycleCount { get; init; }

    public bool IsAvailable { get; init; }

    public string DisplayValue => NumericValue is double numericValue
        ? UnitSymbol is { Length: > 0 }
            ? $"{numericValue:N1} {UnitSymbol}"
            : $"{numericValue:N1}"
        : "Unavailable";
}
