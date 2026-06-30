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

    public static Brush Get(string resourceKey, Windows.UI.Color fallbackColor)
    {
        return Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            && resource is Brush brush
                ? brush
                : new SolidColorBrush(fallbackColor);
    }
}
