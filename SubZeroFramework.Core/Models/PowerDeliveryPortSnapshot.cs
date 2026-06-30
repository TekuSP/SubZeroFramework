using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

/// <summary>
/// Decoupled USB-C Power Delivery state for a single expansion-card slot, projected from the framework-dotnet
/// <c>FrameworkExpansionCardSlotSnapshot.PowerDelivery</c> so the gRPC boundary and UI do not depend on the
/// native snapshot types. Voltage/current are flattened to primitive volts/amperes (matching the battery path).
/// </summary>
public sealed record PowerDeliveryPortSnapshot
{
    /// <summary>The zero-based expansion-card slot index (0–5).</summary>
    public required int SlotIndex { get; init; }

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
}

/// <summary>A point-in-time projection of every reported USB-C Power Delivery port.</summary>
public sealed record PowerDeliverySnapshot
{
    /// <summary>The reported USB-C ports, ordered by slot index.</summary>
    public required IReadOnlyList<PowerDeliveryPortSnapshot> Ports { get; init; }
}
