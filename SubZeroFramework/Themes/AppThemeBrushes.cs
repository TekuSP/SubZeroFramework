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

    public static Windows.UI.Color StatusSuccessColor { get; } = ColorHelper.FromArgb(255, 108, 203, 95);

    public static Windows.UI.Color StatusWarningColor { get; } = ColorHelper.FromArgb(255, 197, 153, 78);

    public static Windows.UI.Color StatusErrorColor { get; } = ColorHelper.FromArgb(255, 68, 39, 38);

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

    public static Windows.UI.Color TemperatureAccentColor => ChartAccentColor;

    public static Brush Get(string resourceKey, Windows.UI.Color fallbackColor)
    {
        return Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            && resource is Brush brush
                ? brush
                : new SolidColorBrush(fallbackColor);
    }
}
