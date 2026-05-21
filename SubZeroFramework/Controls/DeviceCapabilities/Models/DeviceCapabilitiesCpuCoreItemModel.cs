using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;

using Microsoft.UI.Xaml.Media;

using SkiaSharp;

using SubZeroFramework.Models;
using SubZeroFramework.Presentation;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesCpuCoreItemModel : ObservableObject
{
    public DeviceCapabilitiesCpuCoreItemModel(HardwareInfoCpuCore snapshot)
    {
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(DisplayLoad))]
    [NotifyPropertyChangedFor(nameof(UsageBrush))]
    [NotifyPropertyChangedFor(nameof(UsageStrokePaint))]
    [NotifyPropertyChangedFor(nameof(UsageStrokeHex))]
    public partial HardwareInfoCpuCore Snapshot { get; set; } = default!;

    [ObservableProperty]
    public partial DateTimePoint[] UsageHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] UsageSeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? UsageMinLimit { get; set; }

    [ObservableProperty]
    public partial double? UsageMaxLimit { get; set; }

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public string DisplayName => NormalizeCoreDisplayName(Snapshot.Name);

    public string DisplayLoad => Snapshot.DisplayLoad;

    public Brush UsageBrush => GetUsageBrush(Snapshot.PercentProcessorTime);

    public SolidColorPaint UsageStrokePaint => new(SKColor.Parse(UsageStrokeHex), 2);

    public string UsageStrokeHex => GetUsageStrokeHex(Snapshot.PercentProcessorTime);

    public void UpdateHistory(IReadOnlyList<DateTimePoint> usageHistory, double? minLimit, double? maxLimit, IReadOnlyList<double> separators)
    {
        UsageHistory = [.. usageHistory];
        UsageMinLimit = minLimit;
        UsageMaxLimit = maxLimit;
        UsageSeparators = [.. separators];
    }

    public static string NormalizeCoreDisplayName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "Core";
        }

        var candidate = rawName;
        if (rawName.Contains(',', StringComparison.Ordinal))
        {
            var parts = rawName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                candidate = parts[1];
            }
        }

        candidate = candidate.Trim();
        return candidate.Contains("core", StringComparison.OrdinalIgnoreCase)
            ? candidate
            : $"Core {candidate}";
    }

    private static Brush GetUsageBrush(double usagePercent)
    {
        if (usagePercent < 45d)
        {
            return AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.TemperatureAccentColor);
        }

        if (usagePercent < PresentationDefaults.WarningUsagePercent)
        {
            return AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.TextPrimaryColor);
        }

        if (usagePercent < PresentationDefaults.ErrorUsagePercent)
        {
            return AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
        }

        return AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
    }

    private static string GetUsageStrokeHex(double usagePercent)
    {
        if (usagePercent < 45d)
        {
            return AppThemeBrushes.ChartAccentColorHex;
        }

        if (usagePercent < PresentationDefaults.WarningUsagePercent)
        {
            return AppThemeBrushes.ChartPrimaryColorHex;
        }

        if (usagePercent < PresentationDefaults.ErrorUsagePercent)
        {
            return AppThemeBrushes.ChartWarningColorHex;
        }

        return AppThemeBrushes.ChartErrorColorHex;
    }

    private static string Formatter(DateTime date)
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