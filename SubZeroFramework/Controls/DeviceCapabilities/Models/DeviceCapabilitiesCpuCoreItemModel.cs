using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;

using Microsoft.UI.Xaml.Media;

using SkiaSharp;

using SubZeroFramework.Models;
using SubZeroFramework.Presentation;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesCpuCoreItemModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public DeviceCapabilitiesCpuCoreItemModel(HardwareInfoCpuCore snapshot, IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
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

    public Func<double, string> UsageLabelFormatter => _unitFormattingService.FormatRatioAxisLabel;

    public string DisplayName => NormalizeCoreDisplayName(Snapshot.Name);

    public string DisplayLoad => _unitFormattingService.FormatRatio(Snapshot.PercentProcessorTime, decimals: 1);

    public double UsageAxisMaxLimit => _unitFormattingService.RatioAxisMaximum;

    public Brush UsageBrush => GetUsageBrush(Snapshot.PercentProcessorTime);

    public SolidColorPaint UsageStrokePaint => new(SKColor.Parse(UsageStrokeHex), 2);

    public string UsageStrokeHex => GetUsageStrokeHex(Snapshot.PercentProcessorTime);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLoad))]
    [NotifyPropertyChangedFor(nameof(UsageLabelFormatter))]
    [NotifyPropertyChangedFor(nameof(UsageAxisMaxLimit))]
    private partial int UnitFormattingRevision { get; set; }

    public void UpdateHistory(IReadOnlyList<DateTimePoint> usageHistory, double? minLimit, double? maxLimit, IReadOnlyList<double> separators)
    {
        UsageHistory = [.. usageHistory];
        UsageMinLimit = minLimit;
        UsageMaxLimit = maxLimit;
        UsageSeparators = [.. separators];
    }

    public void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;
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

    // Mockup load tiers: idle muted, light blue, busy amber, saturated red — value and sparkline share the tier.
    private static Brush GetUsageBrush(double usagePercent)
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

    private static string GetUsageStrokeHex(double usagePercent)
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