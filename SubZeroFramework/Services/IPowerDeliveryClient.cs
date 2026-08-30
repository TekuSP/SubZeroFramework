using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

/// <summary>Client-side USB-C Power Delivery state for one expansion-card slot (enum values kept as display strings).</summary>
public sealed record PowerDeliveryPortStatus
{
    public required int SlotIndex { get; init; }

    public required bool IsPresent { get; init; }

    public required bool IsActivePort { get; init; }

    public required bool HasContract { get; init; }

    public required string CState { get; init; }

    public required string PowerRole { get; init; }

    public required string DataRole { get; init; }

    public required string CcPolarity { get; init; }

    public required double VoltageVolts { get; init; }

    public required double CurrentAmperes { get; init; }

    public required bool IsVconnActive { get; init; }

    public required bool IsEprActive { get; init; }

    public required bool IsEprSupported { get; init; }

    public required byte AltModeFlags { get; init; }

    /// <summary>
    /// Expansion card in the slot.
    /// </summary>
    /// <remarks>
    /// The real enum, unlike the display strings around it, because this one is BRANCHED ON rather than
    /// merely shown — and the module-inventory client already parses the same value into the same enum.
    /// </remarks>
    public required FrameworkExpansionCardType CardType { get; init; }

    /// <summary>Static USB-C data-lane capability of this slot (FrameworkUsbCDataLane name).</summary>
    public required string DataLane { get; init; }

    /// <summary>Static DisplayPort capability/version of this slot (FrameworkDisplayPortCapability name).</summary>
    public required string DisplayPortCapability { get; init; }

    /// <summary>Whether this slot supports USB-PD charging (false for power-limited slots).</summary>
    public required bool SupportsCharging { get; init; }

    /// <summary>Maximum charge power in watts (0 when not a charging slot, or undocumented).</summary>
    public required int MaxChargeWatts { get; init; }

    /// <summary>Whether the "higher power consumption" USB-A note applies to this slot.</summary>
    public required bool UsbAHighPower { get; init; }

    /// <summary>Whether a documented capability matrix covers this slot and platform.</summary>
    public required bool CapabilityDocumented { get; init; }

    /// <summary>Where the port lives: "Mainboard" (numbered slots) or "GraphicsModule" (the expansion-bay GPU port).</summary>
    public required string PortSource { get; init; }

    /// <summary>Physical position label (upstream framework-system: "Right Back", "Left Middle", "Graphics module", …);
    /// empty on platforms with no documented mapping.</summary>
    public required string PortPosition { get; init; }

    /// <summary>Whether the port is on the left side of the chassis (upstream: PD ports 2 &amp; 3 are left).</summary>
    public required bool PortIsLeft { get; init; }

    // ── The NEGOTIATED contract ───────────────────────────────────────────────────────────────────────
    // A third thing, distinct from MaxChargeWatts (what the BOARD can do) and from the live voltage and
    // current above (what is flowing now). These are what this cable and this charger actually agreed on.

    /// <summary>Whether the port can source as well as sink power.</summary>
    public bool SupportsDualRole { get; init; }

    /// <summary>Sourcing or sinking, as the PD controller reports it.</summary>
    public FrameworkUsbPowerRole UsbPowerRole { get; init; } = FrameworkUsbPowerRole.Disconnected;

    /// <summary>
    /// How the port is charging — full PD, legacy Type-C, proprietary, or not at all.
    /// </summary>
    /// <remarks>
    /// The real enum, not a display string, because the view model BRANCHES on it. A string switch here
    /// would fall through to a wrong label rather than an unknown one the day a member is renamed — which is
    /// exactly what had happened to CardType.
    /// </remarks>
    public FrameworkUsbChargingType ChargingType { get; init; } = FrameworkUsbChargingType.None;

    /// <summary>Highest voltage in the negotiated contract, or null where none was reported.</summary>
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
    public double EffectiveMaximumPowerWatts => NegotiatedMaximumPowerWatts ?? MaxChargeWatts;

    /// <summary>
    /// Whether the contract falls materially short of what the board supports — a weak charger, or a cable
    /// that cannot carry what both ends could manage.
    /// </summary>
    /// <remarks>
    /// Ten percent of tolerance, so rounding and a charger advertising 99 W into a 100 W slot do not read as
    /// a fault.
    /// </remarks>
    public bool IsNegotiatingBelowCapability => NegotiatedMaximumPowerWatts is double negotiated
        && MaxChargeWatts > 0
        && negotiated < MaxChargeWatts * 0.9d;
}

/// <summary>Streams the live USB-C Power Delivery port state from the service.</summary>
public interface IPowerDeliveryClient
{
    /// <summary>A shared, reconnecting stream of the current set of reported PD ports.</summary>
    IObservable<IReadOnlyList<PowerDeliveryPortStatus>> WatchPorts();
}
