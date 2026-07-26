using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;

using Material.Icons;

using Microsoft.UI.Xaml.Media;

using SkiaSharp;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// One GPU or NPU with its live utilization and recent history, on the Device Capabilities page.
/// </summary>
/// <remarks>
/// Every compute device gets its own card. Deliberately never blended into a single "GPU usage" figure — a 4%
/// integrated GPU next to a 97% discrete one averages into a number that describes neither. The chart members
/// mirror <see cref="DeviceCapabilitiesCpuCoreItemModel"/> so GPUs and NPUs render with the CPU per-core look.
/// </remarks>
public partial class ComputeDeviceUsageCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public ComputeDeviceUsageCardModel(TelemetryChannelId channelId, string displayName, bool isNpu, IUnitFormattingService unitFormattingService)
    {
        ChannelId = channelId;
        _unitFormattingService = unitFormattingService;
        DisplayName = displayName;
        IsNpu = isNpu;
        UsageLabelFormatter = CreateUsageLabelFormatter();
        UsageAxisMaxLimit = unitFormattingService.RatioAxisMaximum;
        RefreshUtilizationDisplay();
    }

    public TelemetryChannelId ChannelId { get; }

    public bool IsNpu { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtilizationBarWidthRatio))]
    [NotifyPropertyChangedFor(nameof(UsageBrush))]
    [NotifyPropertyChangedFor(nameof(UsageStrokePaint))]
    public partial double? UtilizationPercent { get; set; }

    /// <summary>False once the service stops reporting the device (driver reload, dGPU powered down, unplugged).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtilizationBarWidthRatio))]
    [NotifyPropertyChangedFor(nameof(UsageBrush))]
    [NotifyPropertyChangedFor(nameof(UsageStrokePaint))]
    public partial bool IsAvailable { get; set; } = true;

    /// <summary>NPU vs GPU only — the two report the same unit but not the same kind of thing.</summary>
    public MaterialIconKind IconKind => IsNpu ? MaterialIconKind.Chip : MaterialIconKind.ExpansionCardVariant;

    public string KindLabel => IsNpu ? "NPU" : "GPU";

    /// <summary>Position within this kind's card list, set by the page model. The channel index is NOT usable
    /// here — it is stable across ALL compute devices, so a lone NPU behind two GPUs would read "NPU 2".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerLabel))]
    public partial int DisplayIndex { get; set; }

    /// <summary>Short picker label ("NPU 0"), the CPU picker's "Package 0" parity.</summary>
    public string PickerLabel => $"{KindLabel} {DisplayIndex}";

    // Through the unit service like every other quantity: the user can display ratios as percent or fraction.
    // Stored (not computed) so a unit-preference change can re-project it without a source-value change.
    [ObservableProperty]
    public partial string UtilizationDisplay { get; private set; } = "—";

    /// <summary>0–1 for the usage bar. Unavailable devices show an empty bar rather than a stale one.</summary>
    public double UtilizationBarWidthRatio => IsAvailable && UtilizationPercent is double percent
        ? Math.Clamp(percent / 100d, 0d, 1d)
        : 0d;

    public Brush UsageBrush => UsageChartStyle.GetUsageBrush(EffectiveUsagePercent);

    public SolidColorPaint UsageStrokePaint => new(SKColor.Parse(UsageChartStyle.GetUsageStrokeHex(EffectiveUsagePercent)), 2);

    // ----- Usage history sparkline (same members and update shape as the CPU per-core card) -----

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

    [ObservableProperty]
    public partial double UsageAxisMaxLimit { get; private set; }

    public void UpdateHistory(IReadOnlyList<DateTimePoint> usageHistory, double? minLimit, double? maxLimit, IReadOnlyList<double> separators)
    {
        UsageHistory = [.. usageHistory];
        UsageMinLimit = minLimit;
        UsageMaxLimit = maxLimit;
        UsageSeparators = [.. separators];
    }

    public void RefreshUnitFormatting()
    {
        UsageLabelFormatter = CreateUsageLabelFormatter();
        UsageAxisMaxLimit = _unitFormattingService.RatioAxisMaximum;
        RefreshUtilizationDisplay();
    }

    // An unavailable or not-yet-read device renders in the idle tier rather than holding its last busy color.
    private double EffectiveUsagePercent => IsAvailable && UtilizationPercent is double percent ? percent : 0d;

    partial void OnUtilizationPercentChanged(double? value) => RefreshUtilizationDisplay();

    partial void OnIsAvailableChanged(bool value) => RefreshUtilizationDisplay();

    private void RefreshUtilizationDisplay() =>
        UtilizationDisplay = IsAvailable
            ? _unitFormattingService.FormatRatio(UtilizationPercent, unavailableDisplay: "—", decimals: 0)
            : "—";

    // Fresh closure per call so the assignment never no-ops (delegates over the same method/target compare
    // equal); capturing a local gives each delegate a new target, so PropertyChanged fires and the axis rebinds.
    private Func<double, string> CreateUsageLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatRatioAxisLabel(value);
    }
}
