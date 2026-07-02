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
public partial class DeviceCapabilitiesCategoryRailItemModel(int index, string name, MaterialIconKind iconKind) : ObservableObject
{
    private static readonly Color SelectedTint = Color.FromArgb(0x33, 0x0F, 0x6C, 0xBD);

    public int Index { get; } = index;

    public string Name { get; } = name;

    public MaterialIconKind IconKind { get; } = iconKind;

    /// <summary>Instance count badge; 0 or negative hides the badge (e.g. System profile).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountDisplay))]
    [NotifyPropertyChangedFor(nameof(CountVisibility))]
    public partial int Count { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailBackground))]
    [NotifyPropertyChangedFor(nameof(AccentBarVisibility))]
    [NotifyPropertyChangedFor(nameof(NameBrush))]
    public partial bool IsSelected { get; set; }

    public string CountDisplay => Count.ToString();

    public Visibility CountVisibility => Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AccentBarVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Brush RailBackground => IsSelected
        ? new SolidColorBrush(SelectedTint)
        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public Brush NameBrush => IsSelected
        ? AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusErrorColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.StatusErrorColor);
}
