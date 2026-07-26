using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

using Windows.UI;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// One entry in the Device Capabilities category rail (e.g. "CPU" with a count badge). Selection styling is
/// derived here so the rail buttons stay plain XAML; clicking an entry navigates the category sub-region.
/// </summary>
/// <param name="requiresItems">
/// When true, the entry disables itself while its count is 0 — a category whose body would be nothing but an
/// empty state is not worth opening. False for the entries that are meaningful with no instances of their own:
/// Onboard devices (the default landing route) and System profile (no count at all).
/// </param>
public partial class DeviceCapabilitiesCategoryRailItemModel(int index, string name, MaterialIconKind iconKind, bool requiresItems = true) : ObservableObject
{
    private static readonly Color SelectedTint = Color.FromArgb(0x33, 0x0F, 0x6C, 0xBD);

    public int Index { get; } = index;

    public string Name { get; } = name;

    public MaterialIconKind IconKind { get; } = iconKind;

    /// <summary>Instance count badge; 0 or negative hides the badge (e.g. System profile).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountDisplay))]
    [NotifyPropertyChangedFor(nameof(CountVisibility))]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    [NotifyPropertyChangedFor(nameof(RailOpacity))]
    [NotifyPropertyChangedFor(nameof(DisabledTooltip))]
    public partial int Count { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailBackground))]
    [NotifyPropertyChangedFor(nameof(AccentBarVisibility))]
    [NotifyPropertyChangedFor(nameof(NameBrush))]
    public partial bool IsSelected { get; set; }

    /// <summary>False while a count-gated category has nothing to show, which also blocks the rail button.</summary>
    public bool IsEnabled => !requiresItems || Count > 0;

    public string CountDisplay => Count.ToString();

    public Visibility CountVisibility => Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AccentBarVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Dims the whole entry when disabled; the rail's own brushes bypass the default disabled visuals.</summary>
    public double RailOpacity => IsEnabled ? 1d : 0.4d;

    /// <summary>Says why the entry is unavailable on hover; null (no tooltip) while it is enabled.</summary>
    public string? DisabledTooltip => IsEnabled ? null : $"No {Name.ToLowerInvariant()} detected";

    public Brush RailBackground => IsSelected
        ? new SolidColorBrush(SelectedTint)
        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public Brush NameBrush => IsSelected
        ? AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusErrorColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.StatusErrorColor);
}
