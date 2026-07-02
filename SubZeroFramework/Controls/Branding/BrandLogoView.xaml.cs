using System;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using SubZeroFramework.Branding;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Branding;

/// <summary>
/// Renders a hardware vendor's brand logo. Resolution is the two-layer resolver: an explicit <see cref="IconifyId"/>
/// wins; otherwise <see cref="Vendor"/> is mapped to a logo via <see cref="BrandLogoCatalog"/>. When a logo is
/// resolved its rasterized Iconify asset (extracted from the IconifyBundle packs at build time) is shown; when none
/// is shipped for the vendor (e.g. Realtek / unknown) a Material fallback glyph (<see cref="FallbackGlyph"/>, default
/// Ethernet) is shown tinted with <see cref="FallbackBrush"/>.
/// </summary>
public sealed partial class BrandLogoView : UserControl
{
    public BrandLogoView()
    {
        InitializeComponent();
        Refresh();
    }

    public static readonly DependencyProperty VendorProperty = DependencyProperty.Register(
        nameof(Vendor),
        typeof(string),
        typeof(BrandLogoView),
        new PropertyMetadata(null, OnResolutionInputChanged));

    /// <summary>Hardware vendor/manufacturer string (e.g. "AuthenticAMD", "Realtek Semiconductor Corp.").</summary>
    public string? Vendor
    {
        get => (string?)GetValue(VendorProperty);
        set => SetValue(VendorProperty, value);
    }

    public static readonly DependencyProperty IconifyIdProperty = DependencyProperty.Register(
        nameof(IconifyId),
        typeof(string),
        typeof(BrandLogoView),
        new PropertyMetadata(null, OnResolutionInputChanged));

    /// <summary>Explicit Iconify id (e.g. "simple-icons:framework") that overrides vendor-based resolution.</summary>
    public string? IconifyId
    {
        get => (string?)GetValue(IconifyIdProperty);
        set => SetValue(IconifyIdProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize),
        typeof(double),
        typeof(BrandLogoView),
        new PropertyMetadata(24d));

    /// <summary>Width/height of the logo in pixels.</summary>
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    // Default matches the mockups' unknown-vendor treatment (blue type glyph); null would render invisibly.
    public static readonly DependencyProperty FallbackBrushProperty = DependencyProperty.Register(
        nameof(FallbackBrush),
        typeof(Brush),
        typeof(BrandLogoView),
        new PropertyMetadata(AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusWarningColor)));

    /// <summary>Tint for the fallback glyph shown when the vendor has no shipped logo.</summary>
    public Brush? FallbackBrush
    {
        get => (Brush?)GetValue(FallbackBrushProperty);
        set => SetValue(FallbackBrushProperty, value);
    }

    public static readonly DependencyProperty FallbackGlyphProperty = DependencyProperty.Register(
        nameof(FallbackGlyph),
        typeof(MaterialIconKind),
        typeof(BrandLogoView),
        new PropertyMetadata(MaterialIconKind.Ethernet));

    /// <summary>Material glyph shown when no brand logo resolves. Defaults to <see cref="MaterialIconKind.Ethernet"/>.</summary>
    public MaterialIconKind FallbackGlyph
    {
        get => (MaterialIconKind)GetValue(FallbackGlyphProperty);
        set => SetValue(FallbackGlyphProperty, value);
    }

    private static void OnResolutionInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BrandLogoView)d).Refresh();

    private void Refresh()
    {
        var iconifyId = !string.IsNullOrWhiteSpace(IconifyId)
            ? IconifyId
            : BrandLogoCatalog.ResolveIconifyId(Vendor);

        var uri = IconAssetNaming.MsAppxUri(iconifyId);
        if (uri is not null)
        {
            LogoImage.Source = new BitmapImage(new Uri(uri));
            LogoImage.Visibility = Visibility.Visible;
            FallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            LogoImage.Source = null;
            LogoImage.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;
        }
    }
}
