using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.DeviceCapabilities;

/// <summary>
/// One icon-led stat tile on the Device Capabilities page: a colored MDI glyph + secondary label above a large
/// value (the mockup's "every stat has an icon" pattern). The icon defaults to the theme accent
/// (StatusInfoBrush); only values carry state colors, per the design mockup.
/// </summary>
public sealed partial class StatTileView : UserControl
{
    public StatTileView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind),
        typeof(MaterialIconKind),
        typeof(StatTileView),
        new PropertyMetadata(MaterialIconKind.InformationOutline));

    /// <summary>The MDI glyph leading the label.</summary>
    public MaterialIconKind IconKind
    {
        get => (MaterialIconKind)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
        nameof(IconBrush),
        typeof(Brush),
        typeof(StatTileView),
        new PropertyMetadata(AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusWarningColor)));

    /// <summary>Glyph tint; defaults to the theme accent (StatusInfoBrush) per the mockup.</summary>
    public Brush IconBrush
    {
        get => (Brush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(StatTileView),
        new PropertyMetadata(string.Empty));

    /// <summary>Secondary stat label, e.g. "Current clock".</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(string),
        typeof(StatTileView),
        new PropertyMetadata(string.Empty));

    /// <summary>The stat value, e.g. "3,875 MHz".</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush),
        typeof(Brush),
        typeof(StatTileView),
        new PropertyMetadata(AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusErrorColor)));

    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
        nameof(Badge),
        typeof(string),
        typeof(StatTileView),
        new PropertyMetadata(string.Empty, static (d, _) => ((StatTileView)d).OnBadgeChanged()));

    /// <summary>Optional chip shown after the value (e.g. "WQXGA", "QHD"); empty hides it.</summary>
    public string Badge
    {
        get => (string)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public static readonly DependencyProperty BadgeVisibilityProperty = DependencyProperty.Register(
        nameof(BadgeVisibility),
        typeof(Visibility),
        typeof(StatTileView),
        new PropertyMetadata(Visibility.Collapsed));

    /// <summary>Derived from <see cref="Badge"/>; bindable so x:Bind picks up changes.</summary>
    public Visibility BadgeVisibility
    {
        get => (Visibility)GetValue(BadgeVisibilityProperty);
        private set => SetValue(BadgeVisibilityProperty, value);
    }

    private void OnBadgeChanged() =>
        BadgeVisibility = string.IsNullOrWhiteSpace(Badge) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Value foreground; defaults to primary text, override for status-toned values.</summary>
    public Brush ValueBrush
    {
        get => (Brush)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }
}
