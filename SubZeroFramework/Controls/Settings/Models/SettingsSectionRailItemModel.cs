using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

using Windows.UI;

namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// One entry in the Settings sub-navigation card (icon + title + subtitle). Selection styling is derived
/// here so the rail buttons stay plain XAML; clicking an entry navigates the section sub-region.
/// </summary>
public partial class SettingsSectionRailItemModel(int index, string title, string subtitle, MaterialIconKind iconKind) : ObservableObject
{
    private static readonly Color SelectedTint = Color.FromArgb(0x33, 0x0F, 0x6C, 0xBD);

    public int Index { get; } = index;

    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;

    public MaterialIconKind IconKind { get; } = iconKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailBackground))]
    [NotifyPropertyChangedFor(nameof(AccentBarVisibility))]
    [NotifyPropertyChangedFor(nameof(TitleBrush))]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    public partial bool IsSelected { get; set; }

    public Visibility AccentBarVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Brush RailBackground => IsSelected
        ? new SolidColorBrush(SelectedTint)
        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public Brush TitleBrush => IsSelected
        ? AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

    public Brush IconBrush => IsSelected
        ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);
}
