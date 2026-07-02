using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Modules.Models;

/// <summary>
/// One FW16 input-deck module tile: the PNG art card (keyboard / LED matrix / touchpad — the real module art,
/// per the asset rule) with an icon fallback, plus the selected-module hero built from the same descriptor.
/// Ordered by the module's physical <see cref="FrameworkInputModulePosition"/>.
/// </summary>
// FD0001 intentionally suppressed: the tile renders whatever the device itself reported.
#pragma warning disable FD0001
public partial class ModuleDeckCardModel : ObservableObject
{
    private readonly bool _isTouchpadSpacer;

    public ModuleDeckCardModel(FrameworkInputModulePosition position)
    {
        Position = position;
    }

    private ModuleDeckCardModel(FrameworkInputModulePosition position, bool isTouchpadSpacer)
        : this(position)
    {
        _isTouchpadSpacer = isTouchpadSpacer;
        IsSpacer = true;
        BuildPresumedHero();
    }

    /// <summary>A passive spacer the EC cannot observe at all, shown because the physical layout implies it
    /// (flanking the keyboard / touchpad). Selectable like any other tile, with a "Presumed" hero.</summary>
    public static ModuleDeckCardModel CreatePresumedSpacer(FrameworkInputModulePosition position, bool isTouchpadSpacer)
        => new(position, isTouchpadSpacer);

    /// <summary>The physical deck position this tile occupies (drives left-to-right ordering).</summary>
    public FrameworkInputModulePosition Position { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    public partial bool IsSelected { get; set; }

    /// <summary>The latest descriptor for this position.</summary>
    public ModuleDescriptorStatus? Descriptor { get; private set; }

    /// <summary>Whether this position was inferred to hold a passive spacer (connected but no USB identity).</summary>
    public bool IsSpacer { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(DeckImage))]
    [NotifyPropertyChangedFor(nameof(DeckImageVisibility))]
    [NotifyPropertyChangedFor(nameof(FallbackIconVisibility))]
    [NotifyPropertyChangedFor(nameof(ImageWidth))]
    [NotifyPropertyChangedFor(nameof(ImageHeight))]
    [NotifyPropertyChangedFor(nameof(CardWidth))]
    [NotifyPropertyChangedFor(nameof(Hero))]
    private partial int Revision { get; set; }

    private FrameworkModuleDisplayInfo DisplayInfo =>
        FrameworkModuleDisplay.For(Descriptor?.Identity ?? FrameworkModuleIdentity.None);

    public string Name => IsSpacer
        ? _isTouchpadSpacer ? "Touchpad spacer" : "Spacer"
        : DisplayInfo.DisplayName;

    public MaterialIconKind IconKind => IsSpacer ? MaterialIconKind.RectangleOutline : ModuleArt.ResolveIcon(DisplayInfo.IconName);

    /// <summary>The module PNG (asset rule), or null → the icon fallback shows instead.</summary>
    public ImageSource? DeckImage => IsSpacer
        ? _isTouchpadSpacer ? ModuleArt.TouchpadSpacerImage : ModuleArt.SpacerImage
        : Descriptor is { } descriptor ? ModuleArt.DeckImageFor(descriptor.Identity) : null;

    /// <summary>Rendered art box, true to the modules' physical proportions: the top row shares one height
    /// (keyboard wide, spacers narrow strips), and the touchpad matches the keyboard's width. Spacer art is
    /// 120 high everywhere so every spacer tile looks identical (user rule).</summary>
    private (double Width, double Height) ImageBox => IsSpacer
        ? (40, 120)
        : Descriptor?.Identity switch
        {
            FrameworkModuleIdentity.Framework16KeyboardModule => (277, 130),
            FrameworkModuleIdentity.Framework16TouchpadModule => (277, 110),
            FrameworkModuleIdentity.Framework16LedMatrix => (34, 130),
            _ => (110, 130),
        };

    public double ImageWidth => ImageBox.Width;

    public double ImageHeight => ImageBox.Height;

    /// <summary>Every spacer tile shares one FIXED card width (user rule: all spacers identical);
    /// other tiles size to their art (NaN = auto).</summary>
    public double CardWidth => IsSpacer ? 150 : double.NaN;

    public Visibility DeckImageVisibility => DeckImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackIconVisibility => DeckImage is null ? Visibility.Visible : Visibility.Collapsed;

    public Brush CardBorderBrush => IsSelected
        ? AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.TextPrimaryColor);

    /// <summary>The selected-module hero for this deck position; rebuilt (and swapped whole) on every update.</summary>
    public ModuleHeroModel Hero { get; private set; } = new(
        MaterialIconKind.HelpCircleOutline, null, "Input-deck module", string.Empty, false, [], null, []);

    public void Update(ModuleDescriptorStatus descriptor, bool isSpacer = false)
    {
        Descriptor = descriptor;
        IsSpacer = isSpacer;

        // EVERY spacer tile shows the same hero, whether EC-inferred or presumed from the layout (user rule).
        if (isSpacer)
        {
            BuildPresumedHero();
        }
        else
        {
            Rebuild(descriptor);
        }

        Revision++;
    }

    private void BuildPresumedHero()
    {
        Hero = new ModuleHeroModel(
            MaterialIconKind.RectangleOutline,
            LogoVendor: null,
            Title: Name,
            ConfidenceLabel: "Inferred",
            ShowConfidence: true,
            Chips: [],
            PdPillText: null,
            Tiles:
            [
                new ModuleHeroTileModel(MaterialIconKind.RectangleOutline, "Module", Name),
                new ModuleHeroTileModel(MaterialIconKind.Power, "State", "Passive"),
                new ModuleHeroTileModel(MaterialIconKind.Sitemap, "Slot kind", FrameworkModuleDisplay.SlotKindLabel(
                    _isTouchpadSpacer ? FrameworkModuleSlotKind.InputDeckTouchpad : FrameworkModuleSlotKind.InputDeckTopRow)),
                new ModuleHeroTileModel(MaterialIconKind.ShieldCheck, "Confidence", "Inferred"),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Vendor ID", "—"),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Product ID", "—"),
                new ModuleHeroTileModel(MaterialIconKind.Pound, "Board ID", "—"),
            ]);
    }

    private void Rebuild(ModuleDescriptorStatus descriptor)
    {
        var name = Name;
        var icon = IconKind;

        List<ModuleHeroChipModel> chips = [];
        if (descriptor.Flags.HasFlag(FrameworkModuleFlags.BuiltIn))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Chip, "BuiltIn"));
        }

        if (descriptor.Flags.HasFlag(FrameworkModuleFlags.Connected))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Connection, "Connected"));
        }

        if (descriptor.Flags.HasFlag(FrameworkModuleFlags.Active))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Flash, "Active"));
        }

        // Spacers are inferred (connected but no USB identity), so their confidence chip says so.
        var confidence = IsSpacer ? "Inferred" : FrameworkModuleDisplay.ConfidenceLabel(descriptor.Confidence);

        Hero = new ModuleHeroModel(
            icon,
            IsSpacer ? null : DisplayInfo.LogoVendor,
            Title: name,
            ConfidenceLabel: confidence,
            ShowConfidence: true,
            Chips: chips,
            PdPillText: null,
            Tiles:
            [
                new ModuleHeroTileModel(icon, "Module", name),
                new ModuleHeroTileModel(MaterialIconKind.Power, "State", FrameworkModuleDisplay.StateLabel(descriptor.Flags)),
                new ModuleHeroTileModel(MaterialIconKind.Sitemap, "Slot kind", FrameworkModuleDisplay.SlotKindLabel(descriptor.SlotKind)),
                new ModuleHeroTileModel(MaterialIconKind.ShieldCheck, "Confidence", confidence),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Vendor ID", FormatHexId(descriptor.VendorId)),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Product ID", FormatHexId(descriptor.ProductId)),
                new ModuleHeroTileModel(MaterialIconKind.Pound, "Board ID", descriptor.BoardId < 0 ? "—" : descriptor.BoardId.ToString()),
            ]);
    }

    private static string FormatHexId(uint id) => id == 0 ? "—" : $"0x{id:X4}";
}
#pragma warning restore FD0001
