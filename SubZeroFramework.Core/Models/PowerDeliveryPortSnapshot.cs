using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

/// <summary>
/// Decoupled USB-C Power Delivery state for a single expansion-card slot, projected from the framework-dotnet
/// <c>FrameworkExpansionCardSlotSnapshot.PowerDelivery</c> so the gRPC boundary and UI do not depend on the
/// native snapshot types. Voltage/current are flattened to primitive volts/amperes (matching the battery path).
/// </summary>
public sealed record PowerDeliveryPortSnapshot
{
    /// <summary>The zero-based expansion-card slot index (0–5 for mainboard slots; a distinct index for the bay port).</summary>
    public required int SlotIndex { get; init; }

    /// <summary>Where the port physically lives: "Mainboard" (numbered slots) or "GraphicsModule" (expansion bay).</summary>
    public required string PortSource { get; init; }

    /// <summary>Physical position label per upstream framework-system (e.g. "Right Back", "Left Middle",
    /// "Graphics module"); empty on platforms with no documented mapping.</summary>
    public required string PortPosition { get; init; }

    /// <summary>Whether the port is on the left side of the chassis (framework-system: PD ports 2 &amp; 3 are left).</summary>
    public required bool PortIsLeft { get; init; }

    /// <summary>Whether the slot appears populated.</summary>
    public required bool IsPresent { get; init; }

    /// <summary>Whether this is the active charging port.</summary>
    public required bool IsActivePort { get; init; }

    /// <summary>Whether a USB Power Delivery contract is active on this port.</summary>
    public required bool HasPowerDeliveryContract { get; init; }

    /// <summary>The physical USB Type-C connection state.</summary>
    public required FrameworkPowerDeliveryTypeCState CState { get; init; }

    /// <summary>The Power Delivery power role (source / sink).</summary>
    public required FrameworkPowerDeliveryPowerRole PowerRole { get; init; }

    /// <summary>The Power Delivery data role (host / device).</summary>
    public required FrameworkPowerDeliveryDataRole DataRole { get; init; }

    /// <summary>The CC pin orientation.</summary>
    public required FrameworkPowerDeliveryCcPolarity CcPolarity { get; init; }

    /// <summary>The negotiated voltage, in volts.</summary>
    public required double VoltageVolts { get; init; }

    /// <summary>The negotiated current, in amperes.</summary>
    public required double CurrentAmperes { get; init; }

    /// <summary>Whether VCONN is active on this port.</summary>
    public required bool IsVconnActive { get; init; }

    /// <summary>Whether Extended Power Range (EPR) is active.</summary>
    public required bool IsEprActive { get; init; }

    /// <summary>Whether the port supports Extended Power Range (EPR).</summary>
    public required bool IsEprSupported { get; init; }

    /// <summary>Raw EC alt-mode status bits (DP/TBT, HPD, etc.).</summary>
    public required byte AltModeFlags { get; init; }

    /// <summary>The expansion card detected in this slot (FrameworkExpansionCardType name; "Unknown" when none).</summary>
    /// <summary>
    /// The expansion card detected in the slot.
    /// </summary>
    /// <remarks>
    /// The real enum, matching the module-inventory path, which carries this same value. It was a string here
    /// and an enum there, and the display code that had to consume the string ended up switching on member
    /// NAMES — where a rename would silently fall through to a wrong answer rather than an unknown one.
    /// </remarks>
    public required FrameworkExpansionCardType CardType { get; init; }

    /// <summary>Static USB-C data-lane capability of this slot (board spec, independent of the live PD state).</summary>
    public required FrameworkUsbCDataLane DataLane { get; init; }

    /// <summary>Static DisplayPort alt-mode capability/version of this slot (board spec).</summary>
    public required FrameworkDisplayPortCapability DisplayPortCapability { get; init; }

    /// <summary>Whether this slot supports USB Power Delivery charging (false for power-limited slots).</summary>
    public required bool SupportsCharging { get; init; }

    /// <summary>Maximum charge power in watts (0 when not a charging slot, or undocumented).</summary>
    public required int MaxChargeWatts { get; init; }

    /// <summary>Whether the "higher power consumption" USB-A note applies to this slot.</summary>
    public required bool UsbAHighPower { get; init; }

    /// <summary>Whether a documented capability matrix covers this slot and platform.</summary>
    public required bool CapabilityDocumented { get; init; }

    // ── The NEGOTIATED contract ───────────────────────────────────────────────────────────────────────
    // A third thing, distinct from the two already above it: MaxChargeWatts is what the BOARD can do, and
    // VoltageVolts/CurrentAmperes are what is flowing right now. These are what this cable and this charger
    // actually agreed on. Together the three answer "why is this only charging at 45 W", which no one of
    // them can answer alone.

    /// <summary>Whether the port can act as either power role, rather than only consuming.</summary>
    public bool SupportsDualRole { get; init; }

    /// <summary>
    /// Whether the port is sourcing or sinking power, as the PD controller reports it.
    /// </summary>
    /// <remarks>
    /// The real enum, like every other enum on this record. Defaults to <c>Disconnected</c>, which is what a
    /// slot with no PD controller behind it genuinely is — not a missing value to be rendered blank.
    /// </remarks>
    public FrameworkUsbPowerRole UsbPowerRole { get; init; } = FrameworkUsbPowerRole.Disconnected;

    /// <summary>
    /// How the port is charging — full PD, legacy Type-C, proprietary, or not at all.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>None</c>. The library distinguishes that from <c>Unknown</c>, and so must this: "not
    /// charging" and "the controller could not say" send a user looking in different places.
    /// </remarks>
    public FrameworkUsbChargingType ChargingType { get; init; } = FrameworkUsbChargingType.None;

    /// <summary>Highest voltage in the negotiated contract, or null where no contract was reported.</summary>
    public double? NegotiatedMaximumVoltageVolts { get; init; }

    /// <summary>Highest current in the negotiated contract, or null where none was reported.</summary>
    public double? NegotiatedMaximumCurrentAmperes { get; init; }

    /// <summary>Highest power in the negotiated contract, or null where none was reported.</summary>
    public double? NegotiatedMaximumPowerWatts { get; init; }

    /// <summary>The controller's own current limit for the port, or null where none was reported.</summary>
    public double? CurrentLimitAmperes { get; init; }

    /// <summary>
    /// The maximum power worth showing: the negotiated contract, falling back to the board's capability.
    /// </summary>
    /// <remarks>
    /// The contract wins because it is the number that explains the machine's behaviour right now. The board
    /// capability is what it could do with a better charger, which matters only once the user knows the two
    /// differ.
    /// </remarks>
    public double EffectiveMaximumPowerWatts => NegotiatedMaximumPowerWatts ?? MaxChargeWatts;

    /// <summary>
    /// Whether the negotiated contract falls materially short of what the board supports.
    /// </summary>
    /// <remarks>
    /// The interesting case, and the reason the contract is surfaced at all: a weak charger, or a cable that
    /// cannot carry what both ends could manage. Ten percent of tolerance keeps rounding and a charger
    /// advertising 99 W for a 100 W slot from reading as a fault.
    /// </remarks>
    public bool IsNegotiatingBelowCapability => NegotiatedMaximumPowerWatts is double negotiated
        && MaxChargeWatts > 0
        && negotiated < MaxChargeWatts * 0.9d;
}

/// <summary>A point-in-time projection of every reported USB-C Power Delivery port.</summary>
public sealed record PowerDeliverySnapshot
{
    /// <summary>The reported USB-C ports, ordered by slot index.</summary>
    public required IReadOnlyList<PowerDeliveryPortSnapshot> Ports { get; init; }
}
