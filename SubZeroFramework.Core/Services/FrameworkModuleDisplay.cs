using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

/// <summary>
/// Presentation metadata for one <see cref="FrameworkModuleIdentity"/>: how the module is shown anywhere in the
/// UI (name, icon, category grouping, spec chips, description). Authored from the Module Library design
/// reference (`Design Ideas/design_handoff_redesign/Modules - Library.dc.html`) — the library itself is not a
/// user-facing view, but every layout renders modules through this catalog so they always look the same.
/// </summary>
/// <param name="DisplayName">Human-readable module name, e.g. "USB-C" or "Keyboard module".</param>
/// <param name="Category">Grouping label, e.g. "Expansion cards" or "Expansion bay".</param>
/// <param name="IconName">A <c>Material.Icons.MaterialIconKind</c> member name (Core cannot reference the icon
/// package; the UI parses the name and falls back to a generic glyph if it ever fails).</param>
/// <param name="Interface">Short interface/spec label, e.g. "USB4 / Thunderbolt"; "—" when meaningless.</param>
/// <param name="Bus">Bus or provenance label, e.g. "USB4", "PCIe", "Internal"; "—" when meaningless.</param>
/// <param name="PowerDelivery">Power-delivery chip text, e.g. "100 W"; "—" when not applicable.</param>
/// <param name="IsHotSwappable">Whether the module is user-swappable (drives the serviceability label).</param>
/// <param name="Description">One-line description of the module.</param>
/// <param name="LogoVendor">Optional brand-logo vendor keyword (e.g. "AMD") for BrandLogoView; null = MDI icon only.</param>
public sealed record FrameworkModuleDisplayInfo(
    string DisplayName,
    string Category,
    string IconName,
    string Interface,
    string Bus,
    string PowerDelivery,
    bool IsHotSwappable,
    string Description,
    string? LogoVendor = null)
{
    /// <summary>Serviceability label per the design reference ("User-swappable" / "Fixed · soldered").</summary>
    public string Serviceability => IsHotSwappable ? "User-swappable" : "Fixed · soldered";
}

/// <summary>
/// Maps the framework-dotnet module enums to their presentation metadata and labels. Static and total: every
/// enum member has an entry (tested), so no module ever renders blank.
/// </summary>
// FD0001 (platform-specific enum members) is intentionally suppressed: the catalog translates whatever the
// device itself reported, so only the members valid for the running platform are ever hit; the rest are inert.
#pragma warning disable FD0001
public static class FrameworkModuleDisplay
{
    private const string ExpansionCards = "Expansion cards";
    private const string InputDeck = "Input deck modules";
    private const string InternalFixed = "Internal fixed devices";
    private const string ExpansionBay = "Expansion bay";
    private const string SpecialStates = "Special states";
    private const string NotApplicable = "—";

    /// <summary>Presentation metadata for a module identity; unknown values fall back to the None entry.</summary>
    public static FrameworkModuleDisplayInfo For(FrameworkModuleIdentity identity) => identity switch
    {
        FrameworkModuleIdentity.UnknownUsbCOccupant => new("Unknown USB-C occupant", SpecialStates, "HelpRhombusOutline", "Unidentified", "USB", "?", true,
            "A device is drawing power on a USB-C port but could not be identified by SubZero."),
        FrameworkModuleIdentity.DpExpansionCard => new("DisplayPort", ExpansionCards, "Monitor", "DisplayPort 1.4", "EC", NotApplicable, true,
            "Full-size DisplayPort 1.4 display output."),
        FrameworkModuleIdentity.HdmiExpansionCard => new("HDMI", ExpansionCards, "VideoInputHdmi", "HDMI 2.0b", "EC", NotApplicable, true,
            "HDMI 2.0b display output driven over DisplayPort alt-mode."),
        FrameworkModuleIdentity.AudioExpansionCard => new("Audio", ExpansionCards, "Headphones", "3.5 mm combo", "USB", NotApplicable, true,
            "High-impedance 3.5 mm headphone / microphone combo jack."),
        FrameworkModuleIdentity.Framework16KeyboardModule => new("Keyboard module", InputDeck, "KeyboardOutline", "Matrix + backlight", "Internal", NotApplicable, true,
            "Hot-swappable Laptop 16 keyboard module for the top input row."),
        FrameworkModuleIdentity.Framework16LedMatrix => new("LED matrix", InputDeck, "DotsGrid", "9×34 LED", "Internal", NotApplicable, true,
            "9×34 addressable LED matrix spacer module."),
        FrameworkModuleIdentity.Framework16TouchpadModule => new("Touchpad module", InputDeck, "GestureTap", "Precision touchpad", "Internal", NotApplicable, true,
            "Removable precision touchpad module (standard or wide haptic)."),
        FrameworkModuleIdentity.InternalKeyboard => new("Keyboard", InternalFixed, "Keyboard", "Matrix", "Internal", NotApplicable, false,
            "Built-in keyboard on non-modular chassis (Laptop 13 etc.)."),
        FrameworkModuleIdentity.InternalTouchpad => new("Touchpad", InternalFixed, "GestureTap", "Precision touchpad", "Internal", NotApplicable, false,
            "Built-in precision touchpad on non-modular chassis."),
        FrameworkModuleIdentity.FingerprintReader => new("Fingerprint reader", InternalFixed, "Fingerprint", "Match-on-chip", "Internal", NotApplicable, false,
            "Fingerprint sensor integrated in the power button."),
        FrameworkModuleIdentity.Touchscreen => new("Touchscreen", InternalFixed, "Tablet", "Capacitive digitizer", "Internal", NotApplicable, false,
            "Capacitive touch digitizer on touch-enabled panels."),
        FrameworkModuleIdentity.Webcam => new("Webcam", InternalFixed, "Webcam", "1080p MIPI", "Internal", NotApplicable, false,
            "1080p webcam module with hardware privacy switch."),
        FrameworkModuleIdentity.ExpansionBay => new("Expansion bay", ExpansionBay, "ExpansionCardVariant", "PCIe", "PCIe", NotApplicable, true,
            "Expansion bay detected with only a generic classification."),
        FrameworkModuleIdentity.ExpansionBayDualInterposer => new("Dual interposer", ExpansionBay, "DeveloperBoard", "PCIe 4.0 ×8", "PCIe", NotApplicable, true,
            "Dual-slot interposer that carries a discrete GPU board."),
        FrameworkModuleIdentity.ExpansionBaySingleInterposer => new("Single interposer", ExpansionBay, "ExpansionCard", "PCIe 4.0 ×4", "PCIe", NotApplicable, true,
            "Single-slot interposer for storage or accessory boards."),
        FrameworkModuleIdentity.ExpansionBayUmaFans => new("UMA fan shim", ExpansionBay, "Fan", "2× blower", "EC", NotApplicable, true,
            "Cooling shim fitted when no discrete GPU is present (UMA graphics)."),
        FrameworkModuleIdentity.ExpansionBaySsdHolder => new("SSD holder", ExpansionBay, "Harddisk", "2× M.2 NVMe", "PCIe", NotApplicable, true,
            "Bay adapter holding additional M.2 NVMe drives."),
        FrameworkModuleIdentity.ExpansionBayPcieAccessory => new("PCIe accessory", ExpansionBay, "ExpansionCardVariant", "PCIe 4.0", "PCIe", NotApplicable, true,
            "Generic PCIe accessory carrier in the expansion bay."),
        FrameworkModuleIdentity.ExpansionBayAmdGpu => new("AMD GPU", ExpansionBay, "ExpansionCard", "PCIe 4.0 ×8", "PCIe", NotApplicable, true,
            "AMD Radeon RX discrete graphics module on a dual interposer.", LogoVendor: "AMD"),
        FrameworkModuleIdentity.ExpansionBayNvidiaGpu => new("Nvidia GPU", ExpansionBay, "ExpansionCard", "PCIe 4.0 ×8", "PCIe", NotApplicable, true,
            "Nvidia GeForce RTX discrete graphics module on a dual interposer.", LogoVendor: "Nvidia"),
        FrameworkModuleIdentity.ExpansionBayFanOnly => new("Fan-only shim", ExpansionBay, "Fan", "1× blower", "EC", NotApplicable, true,
            "Minimal fan-only bay cover for thermal balance."),
        FrameworkModuleIdentity.UsbAExpansionCard => new("USB-A", ExpansionCards, "UsbPort", "USB-A 3.2 Gen 2", "USB 3.2", NotApplicable, true,
            "Legacy USB Type-A 3.2 Gen 2 (10 Gbps) port for older peripherals."),
        FrameworkModuleIdentity.UsbCExpansionCard => new("USB-C", ExpansionCards, "UsbCPort", "USB4 / Thunderbolt", "USB4", "100 W", true,
            "Raw USB4 port with up to 100 W Power Delivery and DisplayPort alt-mode — the most capable card."),
        FrameworkModuleIdentity.EthernetExpansionCard => new("Ethernet", ExpansionCards, "Ethernet", "RJ45 1 GbE", "USB", NotApplicable, true,
            "Gigabit RJ45 wired networking in a flip-down jack."),
        FrameworkModuleIdentity.Ethernet10GExpansionCard => new("Ethernet 10G", ExpansionCards, "EthernetCable", "RJ45 10 GbE", "USB", NotApplicable, true,
            "10 Gigabit RJ45 networking with active cooling fins."),
        FrameworkModuleIdentity.MicroSdExpansionCard => new("microSD", ExpansionCards, "MicroSd", "UHS-II reader", "USB", NotApplicable, true,
            "microSD UHS-II card reader."),
        FrameworkModuleIdentity.SdExpansionCard => new("SD", ExpansionCards, "Sd", "UHS-II reader", "USB", NotApplicable, true,
            "Full-size SD UHS-II card reader."),
        FrameworkModuleIdentity.SsdExpansionCard => new("Storage (SSD)", ExpansionCards, "Harddisk", "NVMe", "USB", NotApplicable, true,
            "250 GB–1 TB NVMe storage expansion card."),
        _ => new("None", SpecialStates, "CardBulletedOffOutline", NotApplicable, NotApplicable, NotApplicable, false,
            "No module present in this position."),
    };

    /// <summary>Presentation metadata for a slot's identified card type (the same entries the identities use).</summary>
    public static FrameworkModuleDisplayInfo ForCardType(FrameworkExpansionCardType cardType) => For(cardType switch
    {
        FrameworkExpansionCardType.DisplayPort => FrameworkModuleIdentity.DpExpansionCard,
        FrameworkExpansionCardType.Hdmi => FrameworkModuleIdentity.HdmiExpansionCard,
        FrameworkExpansionCardType.Audio => FrameworkModuleIdentity.AudioExpansionCard,
        FrameworkExpansionCardType.UsbA => FrameworkModuleIdentity.UsbAExpansionCard,
        FrameworkExpansionCardType.UsbC => FrameworkModuleIdentity.UsbCExpansionCard,
        FrameworkExpansionCardType.Ethernet => FrameworkModuleIdentity.EthernetExpansionCard,
        FrameworkExpansionCardType.Ethernet10G => FrameworkModuleIdentity.Ethernet10GExpansionCard,
        FrameworkExpansionCardType.MicroSd => FrameworkModuleIdentity.MicroSdExpansionCard,
        FrameworkExpansionCardType.Sd => FrameworkModuleIdentity.SdExpansionCard,
        FrameworkExpansionCardType.Ssd => FrameworkModuleIdentity.SsdExpansionCard,
        _ => FrameworkModuleIdentity.UnknownUsbCOccupant,
    });

    /// <summary>Short slot-kind label, e.g. "Expansion-card slot" or "Input deck · top row".</summary>
    public static string SlotKindLabel(FrameworkModuleSlotKind slotKind) => slotKind switch
    {
        FrameworkModuleSlotKind.UsbCPort => "USB-C port",
        FrameworkModuleSlotKind.InputDeckTopRow => "Input deck · top row",
        FrameworkModuleSlotKind.InputDeckTouchpad => "Input deck · touchpad",
        FrameworkModuleSlotKind.ExpansionBay => "Expansion bay",
        FrameworkModuleSlotKind.InternalFixed => "Internal · fixed",
        FrameworkModuleSlotKind.Detached => "Detached",
        FrameworkModuleSlotKind.UsbCExpansionCardSlot => "Expansion-card slot",
        _ => "None",
    };

    /// <summary>One-line slot-kind description per the design reference.</summary>
    public static string SlotKindDescription(FrameworkModuleSlotKind slotKind) => slotKind switch
    {
        FrameworkModuleSlotKind.UsbCPort => "A raw USB-C / USB4 port exposed directly by the mainboard.",
        FrameworkModuleSlotKind.InputDeckTopRow => "Top input-deck position — keyboard, numpad, LED matrix or spacer.",
        FrameworkModuleSlotKind.InputDeckTouchpad => "The touchpad position within the input deck.",
        FrameworkModuleSlotKind.ExpansionBay => "Rear/under bay for GPU, storage or fan interposers.",
        FrameworkModuleSlotKind.InternalFixed => "A soldered or permanently fixed internal device.",
        FrameworkModuleSlotKind.Detached => "Module is known to the system but currently removed.",
        FrameworkModuleSlotKind.UsbCExpansionCardSlot => "A Framework expansion-card slot fed by a USB-C port.",
        _ => "No module present in this position.",
    };

    /// <summary>Confidence chip text per the design reference ("Confirmed" for directly observed modules).</summary>
    public static string ConfidenceLabel(FrameworkModuleConfidence confidence) => confidence switch
    {
        FrameworkModuleConfidence.Direct => "Confirmed",
        FrameworkModuleConfidence.DerivedStrong => "Likely",
        FrameworkModuleConfidence.DerivedWeak => "Inferred",
        _ => "Unknown",
    };

    /// <summary>Best-effort state label from the module flags (Enabled → Active → Connected → Disconnected).</summary>
    public static string StateLabel(FrameworkModuleFlags flags)
    {
        if (flags.HasFlag(FrameworkModuleFlags.Enabled))
        {
            return "Enabled";
        }

        if (flags.HasFlag(FrameworkModuleFlags.Active))
        {
            return "Active";
        }

        return flags.HasFlag(FrameworkModuleFlags.Connected) ? "Connected" : "Disconnected";
    }
}
#pragma warning restore FD0001
