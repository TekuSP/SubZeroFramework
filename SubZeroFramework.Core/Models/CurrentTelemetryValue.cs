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

    /// <summary>
    /// Where the firmware starts warning about this sensor, in canonical Celsius, or null where it reports
    /// no threshold. Temperature channels only.
    /// </summary>
    /// <remarks>
    /// Describes the sensor rather than the reading, and rides with the reading for the same reason
    /// <see cref="SensorName"/> does: every consumer that has a value in hand needs it, and joining back to
    /// the channel to find it would put a lookup in the path of every tick.
    /// </remarks>
    public double? FirmwareWarnCelsius { get; init; }

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

    // ----- Compute (GPU / NPU) extended telemetry -----
    // Carried on the device's utilization channel rather than as sibling channels, which is the same shape
    // the battery fields use. One channel per device keeps the reading atomic: power, temperature and clock
    // are all measured in the same NVML call, and splitting them across channels would let the UI show a
    // power from one tick beside a clock from another.
    //
    // Every field is null on a device whose source cannot report it — PDH exposes no power, temperature or
    // clock at all, so on Windows these are populated only for the NVIDIA GPU.

    /// <summary>Board power draw.</summary>
    public double? ComputePowerWatts { get; init; }

    /// <summary>Die temperature.</summary>
    public double? ComputeTemperatureCelsius { get; init; }

    /// <summary>Current core (shader) clock.</summary>
    public double? ComputeCoreClockMegahertz { get; init; }

    /// <summary>The device's maximum core clock — the denominator that makes the current clock meaningful.</summary>
    public double? ComputeMaxCoreClockMegahertz { get; init; }

    /// <summary>Video memory in use, in bytes.</summary>
    public double? ComputeVramUsedBytes { get; init; }

    /// <summary>Total video memory, in bytes.</summary>
    public double? ComputeVramTotalBytes { get; init; }

    /// <summary>
    /// Why the device is clocked below its rating, or null when the source could not say.
    /// </summary>
    /// <remarks>
    /// Null and <see cref="ComputeThrottleReasons.None"/> are different answers: None means the source replied
    /// and nothing is holding the clocks back, null means the question could not be asked.
    /// </remarks>
    public ComputeThrottleReasons? ComputeThrottleReasons { get; init; }


    public bool IsAvailable { get; init; }

    public string DisplayValue => NumericValue is double numericValue
        ? UnitSymbol is { Length: > 0 }
            ? $"{numericValue:N1} {UnitSymbol}"
            : $"{numericValue:N1}"
        : "Unavailable";
}
