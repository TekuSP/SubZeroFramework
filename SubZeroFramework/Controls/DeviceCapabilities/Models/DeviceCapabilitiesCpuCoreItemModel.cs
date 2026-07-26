using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;

using Microsoft.UI.Xaml.Media;

using SkiaSharp;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesCpuCoreItemModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public DeviceCapabilitiesCpuCoreItemModel(HardwareInfoCpuCore snapshot, IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        UsageLabelFormatter = CreateUsageLabelFormatter();
        UsageAxisMaxLimit = unitFormattingService.RatioAxisMaximum;
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial HardwareInfoCpuCore Snapshot { get; set; } = default!;

    [ObservableProperty]
    public partial DateTimePoint[] UsageHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] UsageSeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? UsageMinLimit { get; set; }

    [ObservableProperty]
    public partial double? UsageMaxLimit { get; set; }

    public Func<DateTime, string> LabelsFormatter { get; } = UsageChartStyle.FormatElapsedLabel;

    [ObservableProperty]
    public partial Func<double, string> UsageLabelFormatter { get; private set; }

    public string DisplayName => NormalizeCoreDisplayName(Snapshot.Name);

    [ObservableProperty]
    public partial string DisplayLoad { get; private set; } = "--";

    [ObservableProperty]
    public partial double UsageAxisMaxLimit { get; private set; }

    /// <summary>
    /// Tier colour for the load figure and the sparkline stroke.
    /// </summary>
    /// <remarks>
    /// STORED rather than computed, and rebuilt only when the tier changes. As getters these re-evaluated on
    /// every snapshot — once per core, per second — and each evaluation allocated a native-backed
    /// <see cref="SolidColorPaint"/> that nobody disposed. On a many-core machine that was the bulk of the
    /// Skia finalizer load a release CPU trace attributed a quarter of process time to.
    /// </remarks>
    [ObservableProperty]
    public partial Brush UsageBrush { get; private set; } = UsageChartStyle.GetUsageBrush(0d);

    [ObservableProperty]
    public partial SolidColorPaint UsageStrokePaint { get; private set; } = UsageChartStyle.CreateUsageStrokePaint(0d);

    [ObservableProperty]
    public partial string UsageStrokeHex { get; private set; } = UsageChartStyle.GetUsageStrokeHex(0d);

    private void RefreshUsageTier()
    {
        var strokeHex = UsageChartStyle.GetUsageStrokeHex(Snapshot.PercentProcessorTime);
        if (string.Equals(strokeHex, UsageStrokeHex, StringComparison.Ordinal))
        {
            return;
        }

        UsageStrokeHex = strokeHex;
        UsageBrush = UsageChartStyle.GetUsageBrush(Snapshot.PercentProcessorTime);
        // Previous paint intentionally not disposed — LiveCharts may still be drawing with it.
        UsageStrokePaint = UsageChartStyle.CreateUsageStrokePaint(Snapshot.PercentProcessorTime);
    }

    public void UpdateHistory(IReadOnlyList<DateTimePoint> usageHistory, double? minLimit, double? maxLimit, IReadOnlyList<double> separators)
    {
        UsageHistory = [.. usageHistory];
        UsageMinLimit = minLimit;
        UsageMaxLimit = maxLimit;
        UsageSeparators = [.. separators];
    }

    // The DisplayLoad string follows the snapshot's live load; the axis formatter + max follow the unit
    // preference. Assignment raises PropertyChanged only on a real change.
    partial void OnSnapshotChanged(HardwareInfoCpuCore value)
    {
        DisplayLoad = _unitFormattingService.FormatRatio(value.PercentProcessorTime, decimals: 1);
        RefreshUsageTier();
    }

    public void RefreshUnitFormatting()
    {
        UsageLabelFormatter = CreateUsageLabelFormatter();
        UsageAxisMaxLimit = _unitFormattingService.RatioAxisMaximum;
        DisplayLoad = _unitFormattingService.FormatRatio(Snapshot.PercentProcessorTime, decimals: 1);
    }

    // Fresh closure per call so the assignment never no-ops (delegates over the same method/target compare
    // equal); capturing a local gives each delegate a new target, so PropertyChanged fires and the axis rebinds.
    private Func<double, string> CreateUsageLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatRatioAxisLabel(value);
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

}