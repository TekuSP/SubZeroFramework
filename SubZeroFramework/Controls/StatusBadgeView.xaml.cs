using Material.Icons;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Controls;

/// <summary>
/// Filled circular status badge: a coloured disc with a centred Material glyph. Reused for the per-fan
/// status chips (row + header) and the "up to date / no unsaved changes" indicators in the master footer
/// and detail action bar. The disc stays circular by tracking <see cref="Diameter"/> in code-behind.
/// </summary>
public sealed partial class StatusBadgeView : UserControl
{
    public StatusBadgeView()
    {
        this.InitializeComponent();
        UpdateCornerRadius();
    }

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter),
        typeof(double),
        typeof(StatusBadgeView),
        new PropertyMetadata(16d, OnDiameterChanged));

    /// <summary>Outer diameter of the disc in pixels.</summary>
    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(
        nameof(GlyphSize),
        typeof(double),
        typeof(StatusBadgeView),
        new PropertyMetadata(11d));

    /// <summary>Width/height of the centred glyph in pixels.</summary>
    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(StatusBadgeView),
        new PropertyMetadata(null));

    /// <summary>Disc fill colour (e.g. the fan's status brush).</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public static readonly DependencyProperty GlyphForegroundProperty = DependencyProperty.Register(
        nameof(GlyphForeground),
        typeof(Brush),
        typeof(StatusBadgeView),
        new PropertyMetadata(new SolidColorBrush(Colors.White)));

    /// <summary>Glyph colour. Defaults to white; set darker for light/green discs.</summary>
    public Brush GlyphForeground
    {
        get => (Brush)GetValue(GlyphForegroundProperty);
        set => SetValue(GlyphForegroundProperty, value);
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(MaterialIconKind),
        typeof(StatusBadgeView),
        new PropertyMetadata(MaterialIconKind.Check));

    /// <summary>Centred Material icon (e.g. Check / Close).</summary>
    public MaterialIconKind Glyph
    {
        get => (MaterialIconKind)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    private static void OnDiameterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusBadgeView)d).UpdateCornerRadius();

    private void UpdateCornerRadius() => BadgeBorder.CornerRadius = new CornerRadius(Diameter / 2d);
}
