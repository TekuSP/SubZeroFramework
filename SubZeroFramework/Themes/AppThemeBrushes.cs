using System.Collections.Concurrent;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Themes;

public static class AppThemeBrushes
{
    public const string ChartAccentColorHex = "#FF8AB7E8";

    public const string ChartPrimaryColorHex = "#FFD7D8FF";

    public const string ChartWarningColorHex = "#FFC5994E";

    public const string ChartErrorColorHex = "#FF8A5C5B";

    public const string ChartMutedColorHex = "#FF5D5E73";

    public const string ChartSeparatorColorHex = "#485D5E73";

    public const string ChartSubtleAxisLabelColorHex = "#D85D5E73";

    public const string ChartDimAxisLabelColorHex = "#C8D7D8FF";

    public static Windows.UI.Color BrandDisabledColor { get; } = ColorHelper.FromArgb(255, 74, 76, 89);

    public static Windows.UI.Color CardBackgroundColor { get; } = ColorHelper.FromArgb(255, 46, 46, 46);

    public static Windows.UI.Color CardSelectedBackgroundColor { get; } = ColorHelper.FromArgb(255, 0, 120, 215);

    public static Windows.UI.Color StatusSuccessColor { get; } = ColorHelper.FromArgb(255, 108, 203, 95);

    public static Windows.UI.Color StatusWarningColor { get; } = ColorHelper.FromArgb(255, 197, 153, 78);

    public static Windows.UI.Color StatusErrorColor { get; } = ColorHelper.FromArgb(255, 68, 39, 38);

    /// <summary>Readable danger/critical red for foreground use (design token --sz-danger #d9706a). Unlike
    /// <see cref="StatusErrorColor"/> (a very dark fill), this is bright enough for text and big numerals.</summary>
    public static Windows.UI.Color SeverityCriticalColor { get; } = ColorHelper.FromArgb(255, 217, 112, 106);

    public static Windows.UI.Color StatusInfoColor { get; } = ColorHelper.FromArgb(255, 138, 183, 232);

    public static Windows.UI.Color TextPrimaryColor { get; } = ColorHelper.FromArgb(255, 215, 216, 255);

    public static Windows.UI.Color TextSecondaryColor { get; } = ColorHelper.FromArgb(255, 160, 163, 186);

    public static Windows.UI.Color ChartAccentColor { get; } = ColorHelper.FromArgb(255, 138, 183, 232);

    public static Windows.UI.Color ChartPrimaryColor { get; } = ColorHelper.FromArgb(255, 215, 216, 255);

    public static Windows.UI.Color ChartWarningColor { get; } = ColorHelper.FromArgb(255, 197, 153, 78);

    public static Windows.UI.Color ChartErrorColor { get; } = ColorHelper.FromArgb(255, 138, 92, 91);

    public static Windows.UI.Color ChartMutedColor { get; } = ColorHelper.FromArgb(255, 93, 94, 115);

    public static Windows.UI.Color ChartSeparatorColor { get; } = ColorHelper.FromArgb(72, 93, 94, 115);

    public static Windows.UI.Color ChartSubtleAxisLabelColor { get; } = ColorHelper.FromArgb(216, 93, 94, 115);

    public static Windows.UI.Color ChartDimAxisLabelColor { get; } = ColorHelper.FromArgb(200, 215, 216, 255);

    // High-contrast variants used by chart paints when their host card is selected
    // (the selected card background switches to accent blue, washing out the default
    // chart line / axis / separator colors).
    public static Windows.UI.Color ChartPrimaryOnSelectedColor { get; } = ColorHelper.FromArgb(255, 255, 255, 255);

    public static Windows.UI.Color ChartErrorOnSelectedColor { get; } = ColorHelper.FromArgb(255, 255, 216, 168);

    public static Windows.UI.Color ChartAxisLabelOnSelectedColor { get; } = ColorHelper.FromArgb(240, 255, 255, 255);

    public static Windows.UI.Color ChartSeparatorOnSelectedColor { get; } = ColorHelper.FromArgb(64, 255, 255, 255);

    public static Windows.UI.Color TemperatureAccentColor => ChartAccentColor;

    // Resolved brushes, keyed by resource key. See Get for why this cache exists and why it never expires.
    private static readonly ConcurrentDictionary<string, Brush> ResolvedBrushes = new(StringComparer.Ordinal);

    /// <summary>
    /// The application brush for a resource key, falling back to a solid brush of the given colour.
    /// </summary>
    /// <remarks>
    /// CACHED, and the cache is the point. This is called from ~157 sites, many of them property getters that
    /// re-evaluate on every telemetry tick, and each call used to reach
    /// <c>Application.Current.Resources</c> — a WinRT projection allocated per call and then abandoned. A
    /// release-build CPU trace of the idle Dashboard attributed 50% of all process CPU to
    /// <c>WinRT.IObjectReference.Finalize</c> clearing exactly that garbage, with the finalizer thread alone
    /// burning half a core.
    ///
    /// Caching changes no object lifetimes: a resource brush is already a single shared instance owned by the
    /// application's ResourceDictionary, so handing out the same reference is what StaticResource does anyway.
    /// Only the repeated lookup goes away.
    ///
    /// The cache never expires because the application pins itself to one theme (App.xaml sets
    /// <c>RequestedTheme="Dark"</c>) and every colour here is a fixed constant. If a light theme or runtime
    /// theme switching is ever added, this cache MUST be invalidated on the theme change — otherwise the UI
    /// keeps the old theme's brushes and the bug looks like "some colours did not update".
    /// </remarks>
    public static Brush Get(string resourceKey, Windows.UI.Color fallbackColor)
    {
        if (ResolvedBrushes.TryGetValue(resourceKey, out var cached))
        {
            return cached;
        }

        var resolved = Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            && resource is Brush brush
                ? brush
                : new SolidColorBrush(fallbackColor);

        // A miss before Application.Current exists would cache a fallback forever, so only a real hit is kept.
        // The fallback is still returned, it just does not poison the cache.
        return Application.Current is null
            ? resolved
            : ResolvedBrushes.GetOrAdd(resourceKey, resolved);
    }
}
