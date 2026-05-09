using System;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Themes;
using SkiaSharp;

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
