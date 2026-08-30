using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

/// <summary>
/// The battery pack's own registers, read over I2C passthrough.
/// </summary>
/// <remarks>
/// <para>
/// EXPENSIVE. Producing this costs many I2C round trips and holds the passthrough while it runs, so it is
/// read on demand only — never on a timer. Ordinary battery telemetry (charge, rate, voltage) comes from the
/// far cheaper power snapshot and is what the live readouts use.
/// </para>
/// <para>
/// A sealed pack answers the basic registers and refuses the manufacturer-access group, so
/// <see cref="StateOfHealthEnergyWattHours"/> is null while everything above it is populated.
/// <see cref="IsUnsealed"/> is what separates "this pack does not publish that" from "the read failed".
/// </para>
/// </remarks>
public sealed record SmartBatterySnapshot
{
    /// <summary>The pack's serial number, as it reports it.</summary>
    public ushort SerialNumber { get; init; }

    /// <summary>When the pack was manufactured, or null where it reported an unusable date.</summary>
    public DateOnly? ManufactureDate { get; init; }

    // These three are Smart Battery Spec STRING registers — the pack returns free text, and the library types
    // them as string for that reason. There is no enum to prefer here: a replacement pack can and does report
    // whatever its manufacturer wrote.

    public string DeviceName { get; init; } = string.Empty;

    public string ManufacturerName { get; init; } = string.Empty;

    public string Chemistry { get; init; } = string.Empty;

    /// <summary>
    /// Whether the pack has been put into shipping cutoff.
    /// </summary>
    /// <remarks>
    /// A cut-off pack is electrically disconnected and will not charge until it is woken — which looks
    /// exactly like a dead battery to anyone who does not know to ask. Worth stating outright.
    /// </remarks>
    public FrameworkBatteryCutoffState CutoffState { get; init; } = FrameworkBatteryCutoffState.Unknown;

    /// <summary>Whether the pack is charging right now.</summary>
    public bool IsCharging { get; init; }

    /// <summary>Whether an adapter is attached.</summary>
    public bool IsAcPresent { get; init; }

    public double TemperatureCelsius { get; init; }

    public double VoltageVolts { get; init; }

    public double CurrentAmperes { get; init; }

    public uint CycleCount { get; init; }

    public double RelativeStateOfChargePercent { get; init; }

    /// <summary>Individual cell voltages. Zero where the pack did not report that cell.</summary>
    public double CellVoltageVolts1 { get; init; }

    public double CellVoltageVolts2 { get; init; }

    public double CellVoltageVolts3 { get; init; }

    public double CellVoltageVolts4 { get; init; }

    /// <summary>The voltage the pack is ASKING the charger for, which may exceed what it is being given.</summary>
    public double ChargingVoltageVolts { get; init; }

    /// <summary>The current the pack is asking for.</summary>
    public double ChargingCurrentAmperes { get; init; }

    /// <summary>Whether the manufacturer-access registers were unlocked and read.</summary>
    public bool IsUnsealed { get; init; }

    /// <summary>The pack's own state-of-health capacity, or null on a sealed pack.</summary>
    public double? StateOfHealthEnergyWattHours { get; init; }

    /// <summary>When this snapshot was taken.</summary>
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// The spread between the highest and lowest reporting cell, or null where fewer than two reported.
    /// </summary>
    /// <remarks>
    /// Reported as a spread because the spread IS the diagnosis. A pack whose cells have drifted apart is
    /// failing regardless of what the total says, and the total is what every other readout in this app
    /// already shows — so four raw numbers would add noise while the one number that matters stayed implicit.
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
    /// <remarks>
    /// Pairs with the cycle count to make wear meaningful: 400 cycles over four years and 400 cycles over
    /// four months describe very different batteries, and the cycle count alone cannot tell them apart.
    /// </remarks>
    public int? AgeInDays => ManufactureDate is { } manufactured
        ? Math.Max(0, DateOnly.FromDateTime(ObservedAt.UtcDateTime).DayNumber - manufactured.DayNumber)
        : null;
}
