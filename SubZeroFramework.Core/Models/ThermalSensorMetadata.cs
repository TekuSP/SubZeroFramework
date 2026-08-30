using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

/// <summary>
/// What the firmware knows about one temperature sensor: its own name for it, and the points at which it
/// acts on it.
/// </summary>
/// <remarks>
/// <para>
/// Static for the life of a connection. Neither a sensor's name nor its thresholds change while the machine
/// runs, and each one costs its own embedded-controller round trip, so these are read once when the
/// connection opens rather than on the telemetry poll.
/// </para>
/// <para>
/// Thresholds are stored in CANONICAL Celsius. Converting for display is the ViewModel's job, through
/// <c>IUnitFormattingService</c> like every other quantity in the app — a model that pre-converted would
/// have to be rebuilt whenever the user changed units.
/// </para>
/// </remarks>
public sealed record ThermalSensorMetadata
{
    /// <summary>Which sensor this describes.</summary>
    public required int SensorIndex { get; init; }

    /// <summary>The firmware's own name for the sensor, or empty where it reports none.</summary>
    public string FirmwareName { get; init; } = string.Empty;

    /// <summary>The library's mapped name, used when the firmware supplies no string of its own.</summary>
    public FrameworkSensorName MappedName { get; init; } = FrameworkSensorName.Unknown;

    /// <summary>What kind of sensor the firmware says this is.</summary>
    public FrameworkTemperatureSensorType SensorType { get; init; } = FrameworkTemperatureSensorType.Ignored;

    /// <summary>Where the firmware starts warning.</summary>
    public double? WarnCelsius { get; init; }

    /// <summary>Where the firmware considers the temperature high.</summary>
    public double? HighCelsius { get; init; }

    /// <summary>Where the firmware will halt the machine.</summary>
    public double? HaltCelsius { get; init; }

    /// <summary>Below this the firmware would stop the fan entirely.</summary>
    public double? FanOffCelsius { get; init; }

    /// <summary>At this the firmware would run the fan flat out.</summary>
    public double? FanMaxCelsius { get; init; }

    /// <summary>
    /// The best name available for this sensor.
    /// </summary>
    /// <remarks>
    /// The firmware's own name wins, because it is the one that matches the service manual and the one a user
    /// searching for their machine will find. The library's mapped name is the fallback, and a bare position
    /// is the last resort — "Temp 3" is where a sensor is, not what it measures, and saying so is better than
    /// inventing a name for it.
    /// </remarks>
    public string DisplayName => !string.IsNullOrWhiteSpace(FirmwareName)
        ? FirmwareName
        : MappedName is not (FrameworkSensorName.Unknown or FrameworkSensorName.Generic)
            ? MappedName.ToString()
            : $"Temp {SensorIndex}";

    /// <summary>Whether the firmware reported any threshold at all for this sensor.</summary>
    public bool HasThresholds => WarnCelsius.HasValue
        || HighCelsius.HasValue
        || HaltCelsius.HasValue
        || FanOffCelsius.HasValue
        || FanMaxCelsius.HasValue;
}
