using FrameworkDotnet.Enums;

namespace SubZeroFramework.Models;

/// <summary>
/// Decoupled descriptor for one detected module or slot occupant, projected from the framework-dotnet
/// <c>FrameworkModuleDescriptorSnapshot</c> / <c>FrameworkExpansionCardSlotSnapshot</c> so the gRPC boundary and
/// UI do not depend on the native snapshot types (same pattern as <see cref="PowerDeliveryPortSnapshot"/>).
/// </summary>
public sealed record ModuleDescriptorSnapshot
{
    /// <summary>The best-effort module classification.</summary>
    public required FrameworkModuleIdentity Identity { get; init; }

    /// <summary>The source or bus that produced the observation.</summary>
    public required FrameworkModuleBus Bus { get; init; }

    /// <summary>The logical slot category.</summary>
    public required FrameworkModuleSlotKind SlotKind { get; init; }

    /// <summary>The classification confidence.</summary>
    public required FrameworkModuleConfidence Confidence { get; init; }

    /// <summary>Whether the slot or module is considered present.</summary>
    public required bool IsPresent { get; init; }

    /// <summary>The zero-based slot index within its group, when applicable.</summary>
    public required int SlotIndex { get; init; }

    /// <summary>Additional module flags (BuiltIn / Active / Connected / HasPdContract / DisplayAltMode / …).</summary>
    public required FrameworkModuleFlags Flags { get; init; }

    /// <summary>The observed vendor ID, when available.</summary>
    public required uint VendorId { get; init; }

    /// <summary>The observed product ID, when available.</summary>
    public required uint ProductId { get; init; }

    /// <summary>The board-specific numeric identifier, when available.</summary>
    public required int BoardId { get; init; }

    /// <summary>The physical input-deck position (Framework Laptop 16), or Unknown when not deck-mounted.</summary>
    public required FrameworkInputModulePosition Position { get; init; }

    /// <summary>The identified expansion card type (USB-C slots only; Unknown elsewhere).</summary>
    public required FrameworkExpansionCardType CardType { get; init; }

    /// <summary>The confidence in the card-type identification (USB-C slots only).</summary>
    public required FrameworkModuleConfidence CardConfidence { get; init; }
}

/// <summary>
/// A point-in-time projection of the full module inventory, grouped the way the Modules page renders it:
/// numbered USB-C expansion-card slots, the FW16 input deck, fixed internals, the expansion bay and detached
/// modules. Projected from <c>FrameworkModuleInventorySnapshot</c> (+ the refined
/// <c>FrameworkExpansionBaySnapshot</c> on Framework 16).
/// </summary>
public sealed record ModuleInventorySnapshot
{
    /// <summary>The reported numbered expansion-card slots, ordered by slot index (present or empty).</summary>
    public required IReadOnlyList<ModuleDescriptorSnapshot> UsbCSlots { get; init; }

    /// <summary>The present FW16 input-deck modules (top row + touchpad; <c>Position</c> tells them apart).</summary>
    public required IReadOnlyList<ModuleDescriptorSnapshot> InputDeckModules { get; init; }

    /// <summary>The present fixed internal devices (keyboard, touchpad, fingerprint, touchscreen, webcam).</summary>
    public required IReadOnlyList<ModuleDescriptorSnapshot> InternalModules { get; init; }

    /// <summary>Modules known to the system but currently removed.</summary>
    public required IReadOnlyList<ModuleDescriptorSnapshot> DetachedModules { get; init; }

    /// <summary>The expansion-bay descriptor (identity refined from the bay snapshot on FW16); null when absent.</summary>
    public required ModuleDescriptorSnapshot? ExpansionBayModule { get; init; }

    /// <summary>The expansion-bay board classification (FW16), or None elsewhere.</summary>
    public required FrameworkExpansionBayBoard ExpansionBayBoard { get; init; }

    /// <summary>The expansion-bay board vendor (FW16), or Unknown elsewhere.</summary>
    public required FrameworkExpansionBayVendor ExpansionBayVendor { get; init; }

    /// <summary>The expansion-bay serial number; empty when absent or unreported.</summary>
    public required string ExpansionBaySerialNumber { get; init; }
}
