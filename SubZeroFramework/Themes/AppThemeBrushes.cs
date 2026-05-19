using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Themes;

public static class AppThemeBrushes
{
    public static Windows.UI.Color BrandDisabledColor { get; } = ColorHelper.FromArgb(255, 74, 76, 89);

    public static Windows.UI.Color StatusSuccessColor { get; } = ColorHelper.FromArgb(255, 108, 203, 95);

    public static Windows.UI.Color StatusWarningColor { get; } = ColorHelper.FromArgb(255, 197, 153, 78);

    public static Windows.UI.Color StatusErrorColor { get; } = ColorHelper.FromArgb(255, 68, 39, 38);

    public static Windows.UI.Color TextPrimaryColor { get; } = ColorHelper.FromArgb(255, 215, 216, 255);

    public static Windows.UI.Color TextSecondaryColor { get; } = ColorHelper.FromArgb(255, 160, 163, 186);

    public static Windows.UI.Color TemperatureAccentColor { get; } = ColorHelper.FromArgb(255, 138, 183, 232);

    public static Brush Get(string resourceKey, Windows.UI.Color fallbackColor)
    {
        return Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            && resource is Brush brush
                ? brush
                : new SolidColorBrush(fallbackColor);
    }
}
