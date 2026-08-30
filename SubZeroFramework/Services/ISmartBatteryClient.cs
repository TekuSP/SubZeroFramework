using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

/// <summary>
/// The battery pack's own registers, as read on demand.
/// </summary>
/// <remarks>
/// A plain record rather than a stream item, because this is never streamed. Producing it costs many I2C
/// round trips to the pack, so it is fetched only when a person asks.
/// </remarks>
public sealed record SmartBatteryStatus
{
    /// <summary>
    /// Whether the pack could be read at all.
    /// </summary>
    /// <remarks>
    /// Deliberately distinct from <see cref="IsUnsealed"/>. "The pack refused the manufacturer registers" and
    /// "the pack did not answer" send a user looking in completely different places.
    /// </remarks>
    public bool IsAvailable { get; init; }

    public ushort SerialNumber { get; init; }

    public DateOnly? ManufactureDate { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string ManufacturerName { get; init; } = string.Empty;

    /// <summary>Smart Battery STRING register — free text from the pack, so not an enum.</summary>
    public string Chemistry { get; init; } = string.Empty;

    public double TemperatureCelsius { get; init; }

    public double VoltageVolts { get; init; }

    public double CurrentAmperes { get; init; }

    public uint CycleCount { get; init; }

    public double RelativeStateOfChargePercent { get; init; }

    public double CellVoltageVolts1 { get; init; }

    public double CellVoltageVolts2 { get; init; }

    public double CellVoltageVolts3 { get; init; }

    public double CellVoltageVolts4 { get; init; }

    /// <summary>What the pack is ASKING the charger for, which may exceed what it is being given.</summary>
    public double ChargingVoltageVolts { get; init; }

    public double ChargingCurrentAmperes { get; init; }

    /// <summary>Whether the manufacturer-access registers were unlocked and read.</summary>
    public bool IsUnsealed { get; init; }

    /// <summary>The pack's own state-of-health capacity, or null on a sealed pack.</summary>
    public double? StateOfHealthEnergyWattHours { get; init; }

    /// <summary>Whether the pack is in shipping cutoff — which looks exactly like a dead battery.</summary>
    public FrameworkBatteryCutoffState CutoffState { get; init; } = FrameworkBatteryCutoffState.Unknown;

    public bool IsCharging { get; init; }

    public bool IsAcPresent { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// The spread between the highest and lowest reporting cell, or null where fewer than two reported.
    /// </summary>
    /// <remarks>
    /// The spread IS the diagnosis: a pack whose cells have drifted apart is failing whatever the total says,
    /// and the total is what every other readout on the page already shows.
    /// </remarks>
    public double? CellImbalanceVolts
    {
        get
        {
            double[] reporting =
            [
                .. new[] { CellVoltageVolts1, CellVoltageVolts2, CellVoltageVolts3, CellVoltageVolts4 }
                    .Where(static volts => volts > 0d),
            ];

            return reporting.Length < 2 ? null : reporting.Max() - reporting.Min();
        }
    }

    /// <summary>How old the pack is, or null where it reported no usable manufacture date.</summary>
    public int? AgeInDays => ManufactureDate is { } manufactured
        ? Math.Max(0, DateOnly.FromDateTime(ObservedAt.UtcDateTime).DayNumber - manufactured.DayNumber)
        : null;
}

/// <summary>Fetches the battery pack's own registers on demand.</summary>
public interface ISmartBatteryClient
{
    /// <summary>
    /// Reads the pack. Slow — drive it from a user action, never a timer.
    /// </summary>
    /// <returns>
    /// A status whose <see cref="SmartBatteryStatus.IsAvailable"/> is false when the pack could not be read.
    /// Never null, and never throws for an ordinary unreadable pack.
    /// </returns>
    Task<SmartBatteryStatus> ReadAsync(CancellationToken cancellationToken = default);
}
