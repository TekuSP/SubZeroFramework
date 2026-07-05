using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Dashboard.Models;

/// <summary>The dashboard cooling profiles: one preset applied to every fan instantly (Custom = per-fan tuned state).</summary>
public enum CoolingPresetKind
{
    Silent,
    Balanced,
    Performance,
    Turbo,
    Custom,
}

/// <summary>
/// One cooling-profile preset card (icon + name + one-line description; selected = accent outline + check).
/// Selection is DERIVED from the live fan control states, so it reflects reality and survives restarts.
/// </summary>
public partial class CoolingPresetCardModel(CoolingPresetKind kind, string name, string description, MaterialIconKind iconKind) : ObservableObject
{
    public CoolingPresetKind Kind { get; } = kind;

    public string Name { get; } = name;

    public string Description { get; } = description;

    public MaterialIconKind IconKind { get; } = iconKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    [NotifyPropertyChangedFor(nameof(CheckVisibility))]
    public partial bool IsSelected { get; set; }

    public Visibility CheckVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    // Brushes are created at binding time (UI thread) — never cached in fields (see uno-vm-thread-affinity).
    public Brush CardBorderBrush => IsSelected
        ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.BrandDisabledColor);
}
