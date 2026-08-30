namespace SubZeroFramework.Models;

public sealed partial record TelemetryChannel
{
    public required TelemetryChannelId Id { get; init; }

    public required string DisplayName { get; init; }

    public string? UnitSymbol { get; init; }

    public DateTimeOffset FirstObservedAt { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }

    public bool IsAvailable { get; init; }

    /// <summary>
    /// The firmware's own action points for this channel's sensor, or null where it reports none.
    /// </summary>
    /// <remarks>
    /// Temperature channels only. Carried on the channel rather than the value because it never changes:
    /// it describes the sensor, not the reading. Canonical Celsius — display conversion is the ViewModel's
    /// job, through the unit service, like every other quantity.
    /// </remarks>
    public FirmwareThermalThresholds? FirmwareThresholds { get; init; }
}

/// <summary>
/// Where the firmware itself acts on a temperature.
/// </summary>
/// <remarks>
/// Not limits this app imposes — limits it must respect. Drawing them on a chart tells a user why their
/// machine behaves as it does at a given temperature, which no setting in this app can explain on its own.
/// </remarks>
public sealed record FirmwareThermalThresholds
{
    public double? WarnCelsius { get; init; }

    public double? HighCelsius { get; init; }

    public double? HaltCelsius { get; init; }

    public double? FanOffCelsius { get; init; }

    public double? FanMaxCelsius { get; init; }

    /// <summary>Whether anything at all was reported. An all-null instance should never be published.</summary>
    public bool HasAny => WarnCelsius.HasValue
        || HighCelsius.HasValue
        || HaltCelsius.HasValue
        || FanOffCelsius.HasValue
        || FanMaxCelsius.HasValue;
}
