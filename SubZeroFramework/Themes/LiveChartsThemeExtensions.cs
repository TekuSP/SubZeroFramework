using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.Themes;

namespace SubZeroFramework.Themes;

public static class LiveChartsThemeExtensions
{
    public static LiveChartsSettings AddMyCustomTheme(this LiveChartsSettings settings) =>
        settings.AddDefaultTheme(theme => theme
            .OnInitialized(() =>
            {
                theme.AnimationsSpeed = TimeSpan.FromMilliseconds(500);
                theme.EasingFunction = EasingFunctions.Lineal;
                theme.Colors = [
                    new(AppThemeBrushes.ChartPrimaryColor.R, AppThemeBrushes.ChartPrimaryColor.G, AppThemeBrushes.ChartPrimaryColor.B),
                    new(AppThemeBrushes.ChartMutedColor.R, AppThemeBrushes.ChartMutedColor.G, AppThemeBrushes.ChartMutedColor.B),
                    new(0, 120, 215)    // PrimaryGraphBrush
                ];
            })
        );
}
