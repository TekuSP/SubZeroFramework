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
    public partial double UsageAxisMaxLimit { get; private set; }

    /// <summary>The usage axis floor, in display units — converted rather than a literal 0, as elsewhere.</summary>
    public double UsageAxisMinPercent => 0d;

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

    partial void OnSnapshotChanged(HardwareInfoCpuCore value) => RefreshUsageTier();

    public void RefreshUnitFormatting()
    {
        UsageLabelFormatter = CreateUsageLabelFormatter();
        UsageAxisMaxLimit = _unitFormattingService.RatioAxisMaximum;

        // The load figure is a CANONICAL percent formatted by UnitFormatConverter at render time, so there is
        // nothing to recompute — the binding just has to run again. A null property name is the framework's
        // own signal for that; the generated x:Bind code re-reads every binding on this source.
        OnPropertyChanged(propertyName: null);
    }

    // Fresh closure per call so the assignment never no-ops (delegates over the same method/target compare
    // equal); capturing a local gives each delegate a new target, so PropertyChanged fires and the axis rebinds.
    private Func<double, string> CreateUsageLabelFormatter()
    {
        // AxisTick, not AxisLabel: the per-core history is converted in DeviceCapabilitiesModel before it
        // reaches this card, so the tick is already in the user's unit.
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatRatioAxisTick(value);
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