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
    public required string CardType { get; init; }

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
}

/// <summary>A point-in-time projection of every reported USB-C Power Delivery port.</summary>
public sealed record PowerDeliverySnapshot
{
    /// <summary>The reported USB-C ports, ordered by slot index.</summary>
    public required IReadOnlyList<PowerDeliveryPortSnapshot> Ports { get; init; }
}
