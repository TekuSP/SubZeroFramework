using System;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// Shared look for the live usage cards (CPU cores, GPUs, NPUs): the load-tier colors and the elapsed-time
/// axis labels, so every usage sparkline on the Device Capabilities page reads the same way.
/// </summary>
public static class UsageChartStyle
{
    // Mockup load tiers: idle muted, light blue, busy amber, saturated red — value and sparkline share the tier.
    public static Brush GetUsageBrush(double usagePercent)
    {
        if (usagePercent <= 1d)
        {
            return AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextPrimaryColor);
        }

        if (usagePercent < 50d)
        {
            return AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.TemperatureAccentColor);
        }

        if (usagePercent < 90d)
        {
            return AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
        }

        return AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
    }

    public static string GetUsageStrokeHex(double usagePercent)
    {
        if (usagePercent <= 1d)
        {
            return AppThemeBrushes.ChartMutedColorHex;
        }

        if (usagePercent < 50d)
        {
            return AppThemeBrushes.ChartAccentColorHex;
        }

        if (usagePercent < 90d)
        {
            return AppThemeBrushes.ChartWarningColorHex;
        }

        // Bright danger tone (StatusErrorTextBrush); the chart-palette error hex is too muted for the mockup.
        return "#D9706A";
    }

    public static string FormatElapsedLabel(DateTime date)
    {
        var elapsed = DateTime.Now - date;

        if (elapsed.TotalSeconds < 1d)
        {
            return "now";
        }

        if (elapsed.TotalMinutes < 1d)
        {
            return $"{elapsed.TotalSeconds:N0}s";
        }

        return $"{elapsed.TotalMinutes:N0}m";
    }
}
