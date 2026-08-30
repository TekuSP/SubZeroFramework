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
    public string DisplayName => FriendlyFirmwareName is { Length: > 0 } firmware
        ? firmware
        : MappedName is not (FrameworkSensorName.Unknown or FrameworkSensorName.Generic)
            ? MappedName.ToString()
            : $"Temp {SensorIndex}";

    /// <summary>
    /// <see cref="FirmwareName"/> reduced to the part that names the SENSOR, or empty when it names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Firmware reports these as wiring, not labels: "apu_f75303@4d" is the APU sensor on an F75303 chip at
    /// I²C address 0x4d. The chip and the address are true and useless — they identify the part that does the
    /// measuring, not the thing being measured, and putting them in a dashboard row spends the whole column
    /// on noise.
    /// </para>
    /// <para>
    /// A trailing "temp" goes too: every sensor on this list measures temperature, so the word distinguishes
    /// nothing. What survives is the subject — "GPU VRAM", "Charger", "APU".
    /// </para>
    /// </remarks>
    public string FriendlyFirmwareName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FirmwareName))
            {
                return string.Empty;
            }

            var name = FirmwareName.Trim();

            // Everything from '@' is the I²C address.
            var addressAt = name.IndexOf('@', StringComparison.Ordinal);
            if (addressAt >= 0)
            {
                name = name[..addressAt];
            }

            // Any segment carrying a digit is a part number ("f75303", "tmp451"). No sensor SUBJECT is
            // spelled with one, so this separates the two without a list of chips to keep up to date.
            var parts = name
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static part => !part.Any(char.IsDigit))
                .ToArray();

            if (parts.Length > 1 && parts[^1].Equals("temp", StringComparison.OrdinalIgnoreCase))
            {
                parts = parts[..^1];
            }

            return parts.Length == 0
                ? string.Empty
                : string.Join(' ', parts.Select(static (part, index) => Prettify(part, isLeading: index == 0)));
        }
    }

    /// <summary>
    /// Renders one segment as a person would write it.
    /// </summary>
    /// <remarks>
    /// Acronyms stay upper case wherever they fall, because "Gpu Vram" reads as a typo. Everything else is
    /// lower case unless it leads, so a name reads as a phrase rather than a Title Cased Label.
    /// </remarks>
    private static string Prettify(string part, bool isLeading)
    {
        if (Acronyms.Contains(part))
        {
            return part.ToUpperInvariant();
        }

        var expanded = part.Equals("amb", StringComparison.OrdinalIgnoreCase) ? "ambient" : part.ToLowerInvariant();

        return isLeading ? string.Concat(char.ToUpperInvariant(expanded[0]), expanded[1..]) : expanded;
    }

    /// <summary>Segments that are initialisms rather than words.</summary>
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "apu", "cpu", "gpu", "vram", "vr", "vrm", "ddr", "soc", "ssd", "pch", "ec", "pd",
    };

    /// <summary>Whether the firmware reported any threshold at all for this sensor.</summary>
    public bool HasThresholds => WarnCelsius.HasValue
        || HighCelsius.HasValue
        || HaltCelsius.HasValue
        || FanOffCelsius.HasValue
        || FanMaxCelsius.HasValue;
}
