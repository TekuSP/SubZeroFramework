using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

public sealed record TemperatureTelemetrySnapshot
{
    public required int SensorIndex { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Platform role of the sensor (e.g. APU, CPU PECI, GPU VRAM); null when not identified.</summary>
    public FrameworkSensorName? SensorName { get; init; }

    public string? UnitSymbol { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public double? TemperatureCelsius { get; init; }

    public FrameworkTemperatureState? TemperatureState { get; init; }

    public bool IsAvailable { get; init; }

    /// <summary>
    /// Where the firmware starts warning about this sensor, in canonical Celsius, or null where it reports
    /// none. Bounds the Adaptive target: a target above it is one the machine will never be left to hold.
    /// </summary>
    public double? FirmwareWarnCelsius { get; init; }
}
