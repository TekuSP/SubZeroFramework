using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.Themes;

namespace SubZeroFramework;

public static class LiveChartsThemeExtensions
{
    public static LiveChartsSettings AddMyCustomTheme(this LiveChartsSettings settings) =>
        settings.AddDefaultTheme(theme => theme
            .OnInitialized(() =>
            {
                theme.AnimationsSpeed = TimeSpan.FromMilliseconds(500);
                theme.EasingFunction = EasingFunctions.Lineal;
                theme.Colors = [
                    new(215, 216, 255), // SecondaryGraphBrush mapped
                    new(93, 94, 115),   // BackgroundGraphBrush
                    new(0, 120, 215)    // PrimaryGraphBrush
                ];
            })
        );
}
