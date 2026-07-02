using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

/// <summary>Client-side descriptor for one detected module or slot occupant (enum names parsed back into the
/// FrameworkDotnet enums so the presentation catalog can be consulted directly).</summary>
public sealed record ModuleDescriptorStatus
{
    public required FrameworkModuleIdentity Identity { get; init; }

    public required FrameworkModuleBus Bus { get; init; }

    public required FrameworkModuleSlotKind SlotKind { get; init; }

    public required FrameworkModuleConfidence Confidence { get; init; }

    public required bool IsPresent { get; init; }

    public required int SlotIndex { get; init; }

    public required FrameworkModuleFlags Flags { get; init; }

    public required uint VendorId { get; init; }

    public required uint ProductId { get; init; }

    public required int BoardId { get; init; }

    /// <summary>The FW16 input-deck position; Unknown for modules that are not deck-mounted.</summary>
    public required FrameworkInputModulePosition Position { get; init; }

    /// <summary>The identified expansion card type (USB-C slots only; Unknown elsewhere).</summary>
    public required FrameworkExpansionCardType CardType { get; init; }

    /// <summary>The confidence in the card-type identification (USB-C slots only).</summary>
    public required FrameworkModuleConfidence CardConfidence { get; init; }
}

/// <summary>Client-side projection of the full module inventory, grouped the way the Modules page renders it.</summary>
public sealed record ModuleInventoryStatus
{
    /// <summary>The reported numbered expansion-card slots in index order (present or empty).</summary>
    public required IReadOnlyList<ModuleDescriptorStatus> UsbCSlots { get; init; }

    /// <summary>The present FW16 input-deck modules (top row + touchpad; <c>Position</c> tells them apart).</summary>
    public required IReadOnlyList<ModuleDescriptorStatus> InputDeckModules { get; init; }

    /// <summary>The present fixed internal devices (keyboard, touchpad, fingerprint, touchscreen, webcam).</summary>
    public required IReadOnlyList<ModuleDescriptorStatus> InternalModules { get; init; }

    /// <summary>Modules known to the system but currently removed.</summary>
    public required IReadOnlyList<ModuleDescriptorStatus> DetachedModules { get; init; }

    /// <summary>The expansion-bay descriptor (identity refined on FW16); null when the chassis has no bay.</summary>
    public required ModuleDescriptorStatus? ExpansionBayModule { get; init; }

    /// <summary>The expansion-bay board classification (FW16), or Unknown elsewhere.</summary>
    public required FrameworkExpansionBayBoard ExpansionBayBoard { get; init; }

    /// <summary>The expansion-bay board vendor (FW16), or Unknown elsewhere.</summary>
    public required FrameworkExpansionBayVendor ExpansionBayVendor { get; init; }

    /// <summary>The expansion-bay serial number; empty when absent or unreported.</summary>
    public required string ExpansionBaySerialNumber { get; init; }
}

/// <summary>Streams the live module inventory from the service.</summary>
public interface IModuleInventoryClient
{
    /// <summary>A shared, reconnecting stream of the current module inventory; null while unavailable.</summary>
    IObservable<ModuleInventoryStatus?> WatchInventory();
}
