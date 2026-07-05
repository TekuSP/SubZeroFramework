using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// One pill in a Display-units segmented picker. The selected pill fills with the brand accent; the rest
/// stay transparent over the group's recessed well.
/// </summary>
public partial class UnitPreferenceSegmentModel(string key, string label, string description, UnitPreferenceRowModel owner) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    /// <summary>Full option description, surfaced as the pill's tooltip.</summary>
    public string Description { get; } = description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Background))]
    [NotifyPropertyChangedFor(nameof(Foreground))]
    public partial bool IsSelected { get; set; }

    // Brushes are created at binding time (UI thread) — never cached in fields, because the owning model
    // may be constructed off the UI thread where SolidColorBrush creation is illegal.
    public Brush Background => IsSelected
        ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public Brush Foreground => IsSelected
        ? AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

    public void Select() => owner.SelectOption(this);
}
