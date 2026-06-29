using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Controls.Fans.Models;

namespace SubZeroFramework.Controls.Fans;

/// <summary>
/// Pill showing a fan's status: a coloured rounded chip with a <see cref="StatusBadgeView"/> disc and the
/// status label, both driven by the fan's status brush/icon/label. Reused by the fan list rows and the fan
/// detail header so the chip stays identical in both places. Sizing is parameterised so each site keeps its
/// own scale.
/// </summary>
public sealed partial class FanStatusChipView : UserControl
{
    public FanStatusChipView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty FanProperty = DependencyProperty.Register(
        nameof(Fan),
        typeof(FanCardModel),
        typeof(FanStatusChipView),
        new PropertyMetadata(null));

    /// <summary>The fan whose status (chip background, brush, icon, label) is shown.</summary>
    public FanCardModel? Fan
    {
        get => (FanCardModel?)GetValue(FanProperty);
        set => SetValue(FanProperty, value);
    }

    public static readonly DependencyProperty BadgeDiameterProperty = DependencyProperty.Register(
        nameof(BadgeDiameter),
        typeof(double),
        typeof(FanStatusChipView),
        new PropertyMetadata(15d));

    /// <summary>Diameter of the status disc.</summary>
    public double BadgeDiameter
    {
        get => (double)GetValue(BadgeDiameterProperty);
        set => SetValue(BadgeDiameterProperty, value);
    }

    public static readonly DependencyProperty BadgeGlyphSizeProperty = DependencyProperty.Register(
        nameof(BadgeGlyphSize),
        typeof(double),
        typeof(FanStatusChipView),
        new PropertyMetadata(10d));

    /// <summary>Glyph size inside the status disc.</summary>
    public double BadgeGlyphSize
    {
        get => (double)GetValue(BadgeGlyphSizeProperty);
        set => SetValue(BadgeGlyphSizeProperty, value);
    }

    public static readonly DependencyProperty LabelFontSizeProperty = DependencyProperty.Register(
        nameof(LabelFontSize),
        typeof(double),
        typeof(FanStatusChipView),
        new PropertyMetadata(11d));

    /// <summary>Font size of the status label.</summary>
    public double LabelFontSize
    {
        get => (double)GetValue(LabelFontSizeProperty);
        set => SetValue(LabelFontSizeProperty, value);
    }

    public static readonly DependencyProperty ChipPaddingProperty = DependencyProperty.Register(
        nameof(ChipPadding),
        typeof(Thickness),
        typeof(FanStatusChipView),
        new PropertyMetadata(new Thickness(8, 2, 8, 2)));

    /// <summary>Inner padding of the chip border.</summary>
    public Thickness ChipPadding
    {
        get => (Thickness)GetValue(ChipPaddingProperty);
        set => SetValue(ChipPaddingProperty, value);
    }

    public static readonly DependencyProperty ChipSpacingProperty = DependencyProperty.Register(
        nameof(ChipSpacing),
        typeof(double),
        typeof(FanStatusChipView),
        new PropertyMetadata(5d));

    /// <summary>Spacing between the status disc and the label.</summary>
    public double ChipSpacing
    {
        get => (double)GetValue(ChipSpacingProperty);
        set => SetValue(ChipSpacingProperty, value);
    }
}
