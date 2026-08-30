using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Modules.Models;

/// <summary>
/// One numbered expansion-card slot on a chassis map: the compact card (number badge, card-type icon, name,
/// empty/selected states) plus the selected-module hero built from the same data. Fed by the module-inventory
/// descriptor joined with its PD port (position/negotiated power) by slot index.
/// </summary>
// FD0001 intentionally suppressed: the card renders whatever the device itself reported.
#pragma warning disable FD0001
public partial class ModuleSlotCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;
    private int _displayNumber;

    /// <param name="unitFormattingService">Formats the negotiated PD wattage in the user's chosen power unit.</param>
    public ModuleSlotCardModel(int slotIndex, IUnitFormattingService unitFormattingService, bool isUnreported = false)
    {
        _unitFormattingService = unitFormattingService;
        SlotIndex = slotIndex;
        IsUnreported = isUnreported;
        _displayNumber = slotIndex + 1;
        SlotNumberDisplay = _displayNumber.ToString();
        Rebuild();
    }

    /// <summary>The zero-based EC slot index (join key for the inventory/PD data).</summary>
    public int SlotIndex { get; }

    /// <summary>Physical slot the EC does not report yet (FW16 non-PD slots) — rendered as a "No data" ghost.</summary>
    public bool IsUnreported { get; }

    /// <summary>The physical slot number shown on the card (left column 1..3 back→front, right 4..6),
    /// assigned by the page model after the chassis-side split — distinct from the EC index.</summary>
    [ObservableProperty]
    public partial string SlotNumberDisplay { get; private set; }

    public void SetDisplayNumber(int displayNumber)
    {
        if (_displayNumber == displayNumber)
        {
            return;
        }

        _displayNumber = displayNumber;
        SlotNumberDisplay = displayNumber.ToString();
        Rebuild();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    public partial bool IsSelected { get; set; }

    /// <summary>The latest inventory descriptor for this slot; null until reported. Assigning it re-raises
    /// the descriptor-derived displays; Hero is rebuilt and reassigned separately.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(ContentOpacity))]
    public partial ModuleDescriptorStatus? Descriptor { get; private set; }

    /// <summary>The joined PD port state for this slot; null until reported.</summary>
    public PowerDeliveryPortStatus? PdPort { get; private set; }

    /// <summary>Whether the slot sits on the left side of the chassis (from the PD port capability;
    /// FW16 fallback: EC ports 2 &amp; 3 are left).</summary>
    public bool IsLeftSide => PdPort?.PortIsLeft ?? SlotIndex is 2 or 3;

    public bool IsEmpty => Descriptor is not { IsPresent: true };

    private FrameworkModuleDisplayInfo DisplayInfo => Descriptor is { IsPresent: true } descriptor
        ? FrameworkModuleDisplay.ForCardType(descriptor.CardType)
        : FrameworkModuleDisplay.For(FrameworkModuleIdentity.None);

    public string Name => IsUnreported
        ? "No data"
        : IsEmpty ? "Nothing detected" : DisplayInfo.DisplayName;

    public MaterialIconKind IconKind => IsUnreported
        ? MaterialIconKind.HelpRhombusOutline
        : IsEmpty
            ? MaterialIconKind.CardBulletedOffOutline
            : ModuleArt.ResolveIcon(DisplayInfo.IconName);

    /// <summary>Empty slots render as ghosts, per the mockup.</summary>
    public double ContentOpacity => IsEmpty ? 0.55 : 1d;

    public Brush CardBorderBrush => IsSelected
        ? AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.TextPrimaryColor);

    /// <summary>The selected-module hero for this slot; rebuilt (and swapped whole) on every update.</summary>
    [ObservableProperty]
    public partial ModuleHeroModel Hero { get; private set; } = null!;

    public void Update(ModuleDescriptorStatus? descriptor, PowerDeliveryPortStatus? pdPort)
    {
        Descriptor = descriptor;
        PdPort = pdPort;
        Rebuild();
    }

    private void Rebuild()
    {
        var slotNumber = _displayNumber;
        if (IsUnreported)
        {
            Hero = new ModuleHeroModel(
                MaterialIconKind.HelpRhombusOutline,
                LogoVendor: null,
                Title: $"Slot {slotNumber} · Not reported",
                ConfidenceLabel: string.Empty,
                ShowConfidence: false,
                Chips: [],
                PdPillText: null,
                Tiles:
                [
                    new ModuleHeroTileModel(MaterialIconKind.Sitemap, "Slot kind", FrameworkModuleDisplay.SlotKindLabel(FrameworkModuleSlotKind.UsbCExpansionCardSlot)),
                    new ModuleHeroTileModel(MaterialIconKind.HelpRhombusOutline, "Status", "Not reported by the EC yet (non-PD slot)"),
                ]);
            return;
        }

        if (Descriptor is not { IsPresent: true } descriptor)
        {
            Hero = new ModuleHeroModel(
                MaterialIconKind.CardBulletedOffOutline,
                LogoVendor: null,
                Title: $"Slot {slotNumber} · Nothing detected",
                ConfidenceLabel: string.Empty,
                ShowConfidence: false,
                Chips: [],
                PdPillText: BuildPdPillText(),
                Tiles:
                [
                    new ModuleHeroTileModel(MaterialIconKind.Sitemap, "Slot kind", FrameworkModuleDisplay.SlotKindLabel(FrameworkModuleSlotKind.UsbCExpansionCardSlot)),
                    new ModuleHeroTileModel(MaterialIconKind.CardBulletedOffOutline, "Status", "Empty, or a passive pass-through card (USB-C / USB-A) with nothing plugged in — the EC cannot tell them apart"),
                ]);
            return;
        }

        var info = FrameworkModuleDisplay.ForCardType(descriptor.CardType);
        var title = descriptor.CardType == FrameworkExpansionCardType.Unknown
            ? $"Slot {slotNumber} · Unidentified occupant"
            : $"Slot {slotNumber} · {info.DisplayName} expansion card";

        List<ModuleHeroChipModel> chips = [];
        if (descriptor.Flags.HasFlag(FrameworkModuleFlags.Connected))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Connection, "Connected"));
        }

        if (descriptor.Flags.HasFlag(FrameworkModuleFlags.Active))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Flash, "Active"));
        }

        if (descriptor.Flags.HasFlag(FrameworkModuleFlags.HasPdContract))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.PowerPlug, "PD contract"));
        }

        if (descriptor.Flags.HasFlag(FrameworkModuleFlags.DisplayAltMode))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Monitor, "DP alt-mode"));
        }

        Hero = new ModuleHeroModel(
            ModuleArt.ResolveIcon(info.IconName),
            info.LogoVendor,
            Title: title,
            ConfidenceLabel: FrameworkModuleDisplay.ConfidenceLabel(descriptor.CardConfidence),
            ShowConfidence: true,
            Chips: chips,
            PdPillText: BuildPdPillText(),
            Tiles:
            [
                new ModuleHeroTileModel(ModuleArt.ResolveIcon(info.IconName), "Card type", $"{info.DisplayName} ({info.Interface})"),
                new ModuleHeroTileModel(MaterialIconKind.ShieldCheck, "Card confidence", FrameworkModuleDisplay.ConfidenceLabel(descriptor.CardConfidence)),
                new ModuleHeroTileModel(MaterialIconKind.Sitemap, "Slot kind", FrameworkModuleDisplay.SlotKindLabel(descriptor.SlotKind)),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Vendor ID", FormatHexId(descriptor.VendorId)),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Product ID", FormatHexId(descriptor.ProductId)),
                new ModuleHeroTileModel(MaterialIconKind.Pound, "Board ID", descriptor.BoardId < 0 ? "—" : descriptor.BoardId.ToString()),
                new ModuleHeroTileModel(MaterialIconKind.Memory, "Firmware", FirmwareVersion.Length > 0 ? FirmwareVersion : "—"),
            ]);
    }

    /// <summary>
    /// What this module is running, or empty where nothing reported a version for it.
    /// </summary>
    /// <remarks>
    /// Pushed in by the page, which resolves it by vendor and product id. Not by slot index: the peripheral
    /// descriptors number slots by USB enumeration order, which bears no relation to the physical position
    /// this card draws, and matching on it would put a confident wrong version under a picture of a slot.
    /// </remarks>
    public string FirmwareVersion { get; set; } = string.Empty;

    private static string FormatHexId(uint id) => id == 0 ? "—" : $"0x{id:X4}";

    private string BuildPdPillText()
    {
        if (PdPort is not { } port)
        {
            return "No PD data";
        }

        if (!port.HasContract)
        {
            return "No PD contract";
        }

        var negotiatedWatts = (int)Math.Round(port.VoltageVolts * port.CurrentAmperes);
        var watts = negotiatedWatts > 0 ? negotiatedWatts : port.MaxChargeWatts;
        var altMode = Descriptor?.Flags.HasFlag(FrameworkModuleFlags.DisplayAltMode) == true ? " · DP alt-mode" : string.Empty;

        if (watts <= 0)
        {
            return $"PD contract{altMode}";
        }

        // A composite (wattage plus an alt-mode note), so it is formatted here rather than by a converter —
        // but through the service, so it follows the user's power-unit preference like every other wattage.
        return $"{_unitFormattingService.FormatPowerWatts(watts)}{altMode}";
    }
}
#pragma warning restore FD0001
