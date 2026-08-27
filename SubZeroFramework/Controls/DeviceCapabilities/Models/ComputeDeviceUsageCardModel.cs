using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SkiaSharp;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

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
        RefreshEffectiveUtilization();
    }

    public TelemetryChannelId ChannelId { get; }

    public bool IsNpu { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtilizationBarWidthRatio))]
    public partial double? UtilizationPercent { get; set; }

    /// <summary>False once the service stops reporting the device (driver reload, dGPU powered down, unplugged).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UtilizationBarWidthRatio))]
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

    /// <summary>
    /// Static identity for this device from the hardware inventory, when one matched.
    /// </summary>
    /// <remarks>
    /// Matched by display name, which is exact rather than heuristic here: the utilization reader and the
    /// inventory resolver derive the name from the SAME platform source on each OS, so the strings are equal
    /// by construction. A device with no match keeps its live reading and shows "Unknown" details.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VendorDisplay))]
    [NotifyPropertyChangedFor(nameof(DriverDisplay))]
    [NotifyPropertyChangedFor(nameof(DriverVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(FirmwareVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(LocationDisplay))]
    [NotifyPropertyChangedFor(nameof(DescriptionDisplay))]
    public partial HardwareInfoComputeAccelerator? Accelerator { get; set; }

    public string VendorDisplay => Accelerator?.DisplayVendor ?? "Unknown";

    public string DriverDisplay => Accelerator?.DisplayDriver ?? "Unknown";

    public string DriverVersionDisplay => Accelerator?.DisplayDriverVersion ?? "Unknown";

    public string FirmwareVersionDisplay => Accelerator?.DisplayFirmwareVersion ?? "Unknown";

    public string LocationDisplay => Accelerator?.DisplayLocation ?? "Unknown";

    public string DescriptionDisplay => Accelerator?.DisplayDescription ?? "Unknown";

    // ----- Extended telemetry (power, temperature, clock, throttle) -----
    // Reported only where the source can measure it. On Windows the PDH counter set carries none of this, so
    // these are populated for an NVIDIA GPU via NVML and stay empty for AMD/Intel adapters — a real platform
    // limitation, shown as "--" rather than a zero that would read as a genuine measurement.

    // Everything here is CANONICAL: watts, Celsius, megahertz, bytes, percent. Nothing is formatted, because
    // formatting is the converter's job — see UnitFormatConverter. What stays in the view model is only what
    // a converter cannot express: values DERIVED from more than one source, and visibility.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtendedTelemetry))]
    [NotifyPropertyChangedFor(nameof(ExtendedTelemetryVisibility))]
    public partial double? PowerWatts { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtendedTelemetry))]
    [NotifyPropertyChangedFor(nameof(ExtendedTelemetryVisibility))]
    public partial double? TemperatureCelsius { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtendedTelemetry))]
    [NotifyPropertyChangedFor(nameof(ExtendedTelemetryVisibility))]
    [NotifyPropertyChangedFor(nameof(CoreClockRatioPercent))]
    [NotifyPropertyChangedFor(nameof(CoreClockBarValue))]
    [NotifyPropertyChangedFor(nameof(CoreClockBarVisibility))]
    public partial double? CoreClockMegahertz { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoreClockRatioPercent))]
    [NotifyPropertyChangedFor(nameof(CoreClockBarValue))]
    [NotifyPropertyChangedFor(nameof(CoreClockBarVisibility))]
    public partial double? MaxCoreClockMegahertz { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtendedTelemetry))]
    [NotifyPropertyChangedFor(nameof(ExtendedTelemetryVisibility))]
    [NotifyPropertyChangedFor(nameof(ThrottleDisplay))]
    [NotifyPropertyChangedFor(nameof(IsThermallyThrottled))]
    public partial ComputeThrottleReasons? ThrottleReasons { get; set; }

    // Video memory. Memory USED, not memory-bandwidth utilisation: NVML can report both, ADLX only the
    // former, and a figure that means something different per vendor is worse than one that means the same
    // thing everywhere.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtendedTelemetry))]
    [NotifyPropertyChangedFor(nameof(ExtendedTelemetryVisibility))]
    [NotifyPropertyChangedFor(nameof(VramUtilizationPercent))]
    [NotifyPropertyChangedFor(nameof(VramBarValue))]
    [NotifyPropertyChangedFor(nameof(VramVisibility))]
    [NotifyPropertyChangedFor(nameof(VramColumnWidth))]
    [NotifyPropertyChangedFor(nameof(HasVramTotal))]
    public partial double? VramUsedBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VramUtilizationPercent))]
    [NotifyPropertyChangedFor(nameof(VramBarValue))]
    [NotifyPropertyChangedFor(nameof(HasVramTotal))]
    public partial double? VramTotalBytes { get; set; }

    // ----- Derived from more than one source, so a converter cannot produce them. -----

    /// <summary>True when this device reports anything beyond utilization, so the UI can hide an empty block.</summary>
    public bool HasExtendedTelemetry
        => PowerWatts is not null
            || TemperatureCelsius is not null
            || CoreClockMegahertz is not null
            || ThrottleReasons is not null
            || VramUsedBytes is not null;

    public Visibility ExtendedTelemetryVisibility => HasExtendedTelemetry ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Current clock as a percentage of the maximum. Canonical (percent), so the UI formats it through the
    /// Ratio converter like any other ratio, and NOT clamped — a GPU boosting past the maximum its driver
    /// reports is real (see NvmlReadingPlausibility.ClockHeadroomFactor) and clamping would hide it.
    /// </summary>
    public double? CoreClockRatioPercent
        => CoreClockMegahertz is { } clock && MaxCoreClockMegahertz is { } maximum && maximum > 0d
            ? clock / maximum * 100d
            : null;

    /// <summary>
    /// The bar's value, 0-1. Clamped where <see cref="CoreClockRatioPercent"/> is not, because a bar cannot
    /// draw past its end; non-nullable because <c>ProgressBar.Value</c> is, with the unknown case carried by
    /// <see cref="CoreClockBarVisibility"/> rather than by a zero that would read as 0 MHz.
    /// </summary>
    public double CoreClockBarValue => Math.Clamp((CoreClockRatioPercent ?? 0d) / 100d, 0d, 1d);

    /// <summary>Hidden unless both ends of the ratio are known — a bar with one end missing is meaningless.</summary>
    public Visibility CoreClockBarVisibility => CoreClockRatioPercent is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Not a unit-bearing quantity, so it stays a view-model string rather than a converter.</summary>
    public string ThrottleDisplay => ComputeThrottleReasonsDisplay.Describe(ThrottleReasons);

    /// <summary>Video memory in use as a percentage of the total — canonical, formatted by the Ratio converter.</summary>
    public double? VramUtilizationPercent
        => VramUsedBytes is { } used && VramTotalBytes is { } total && total > 0d
            ? Math.Clamp(used / total * 100d, 0d, 100d)
            : null;

    public double VramBarValue => (VramUtilizationPercent ?? 0d) / 100d;

    /// <summary>Hidden entirely for a device that reports no video memory at all.</summary>
    public Visibility VramVisibility => VramUsedBytes is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gates the "of {total}" half of the summary, which is absent on a device that reports no total.</summary>
    public Visibility HasVramTotal => VramTotalBytes is not null ? Visibility.Visible : Visibility.Collapsed;

    // Byte formatting deliberately absent: memory sizes go through the InformationSize converter like every
    // other quantity, so the user's binary/decimal preference applies here as it does on the Memory page.

    /// <summary>Temperature throttling specifically — the one more airflow actually fixes.</summary>
    public bool IsThermallyThrottled => ThrottleReasons?.HasFlag(ComputeThrottleReasons.ThermalLimit) == true;

    /// <summary>
    /// Highlights the throttle line when temperature is the cause.
    /// </summary>
    /// <remarks>
    /// STORED and updated on change, not computed. A getter would allocate a brush on every binding
    /// evaluation — the same trap the usage tier documents above, where per-tick paint allocation showed up
    /// in a release trace as a quarter of process time in Skia finalizers.
    /// </remarks>
    [ObservableProperty]
    public partial Brush ThrottleBrush { get; private set; } = UsageChartStyle.GetUsageBrush(0d);

    partial void OnThrottleReasonsChanged(ComputeThrottleReasons? value)
        => ThrottleBrush = UsageChartStyle.GetUsageBrush(IsThermallyThrottled ? 95d : 0d);


    /// <summary>
    /// Canonical utilization percent for the big readout, formatted by UnitFormatConverter (the
    /// UnitFormatDash instance: an unavailable or not-yet-read device shows "—", not a stale figure).
    /// </summary>
    [ObservableProperty]
    public partial double? EffectiveUtilizationPercent { get; private set; }

    /// <summary>0–1 for the usage bar. Unavailable devices show an empty bar rather than a stale one.</summary>
    public double UtilizationBarWidthRatio => IsAvailable && UtilizationPercent is double percent
        ? Math.Clamp(percent / 100d, 0d, 1d)
        : 0d;

    /// <summary>
    /// Tier colour for the value text and the sparkline stroke.
    /// </summary>
    /// <remarks>
    /// STORED, not computed. These used to be getters, so every utilization change re-evaluated them and each
    /// evaluation allocated a fresh <see cref="SolidColorPaint"/> wrapping a native Skia object that nobody
    /// disposed. Multiplied by every device and every telemetry tick, that abandoned native garbage showed up
    /// in a release CPU trace as a quarter of all process time spent in Skia finalizers. The paint is now
    /// rebuilt only when the tier actually changes, which is rare — the colour only moves when load crosses
    /// 1%, 50% or 90%.
    /// </remarks>
    [ObservableProperty]
    public partial Brush UsageBrush { get; private set; } = UsageChartStyle.GetUsageBrush(0d);

    [ObservableProperty]
    public partial SolidColorPaint UsageStrokePaint { get; private set; } = UsageChartStyle.CreateUsageStrokePaint(0d);

    /// <summary>Rebuilds the tier visuals, and only allocates a paint when the tier really changed.</summary>
    private void RefreshUsageTier()
    {
        var strokeHex = UsageChartStyle.GetUsageStrokeHex(EffectiveUsagePercent);
        if (string.Equals(strokeHex, _usageStrokeHex, StringComparison.Ordinal))
        {
            return;
        }

        _usageStrokeHex = strokeHex;
        UsageBrush = UsageChartStyle.GetUsageBrush(EffectiveUsagePercent);
        // Deliberately not disposing the previous paint: LiveCharts may still be holding it for a frame, and
        // a use-after-dispose on a native Skia handle is far worse than letting the GC take one object per
        // tier transition.
        UsageStrokePaint = UsageChartStyle.CreateUsageStrokePaint(EffectiveUsagePercent);
    }

    private string? _usageStrokeHex = UsageChartStyle.GetUsageStrokeHex(0d);

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

    /// <summary>
    /// The usage axis floor, in display units.
    /// </summary>
    /// <remarks>
    /// Converted rather than written as a literal 0 even though every ratio unit shares an origin, so no
    /// future reader has to re-derive which zeros are safe to hardcode. Getting that judgement wrong on a
    /// temperature axis is what shipped a broken chart.
    /// </remarks>
    public double UsageAxisMinPercent => 0d;

    // ----- Video-memory history -----
    //
    // Fed from the service's OWN retained series (TelemetryMetric.VramUtilizationPercent), not accumulated
    // here: history belongs to the backend so every chart replays the last window on open instead of
    // starting empty. Shares the utilisation chart's X axis and Y maximum so the two read as one pair.

    [ObservableProperty]
    public partial DateTimePoint[] VramHistory { get; private set; } = [];

    /// <summary>
    /// A distinct stroke from the load-tier usage colour: the two charts sit side by side, and colouring
    /// both by load would read as the same measurement twice.
    /// </summary>
    public SolidColorPaint VramStrokePaint { get; } = new(SKColor.Parse(AppThemeBrushes.ChartAccentColorHex), 2);

    /// <summary>
    /// Width of the video-memory column beside the utilisation chart: an equal half when the device reports
    /// video memory, and zero when it does not — which lets the utilisation chart take the full width rather
    /// than leaving a gap where the second chart would have been.
    /// </summary>
    public GridLength VramColumnWidth => VramVisibility == Visibility.Visible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);

    public void UpdateVramHistory(IReadOnlyList<DateTimePoint> vramHistory)
    {
        VramHistory = [.. vramHistory];
    }

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


        // Everything else on this card is a CANONICAL value formatted by UnitFormatConverter at render time,
        // so there is nothing to recompute — the bindings just have to run again. A null property name is the
        // framework's own signal for that: the generated x:Bind code tests String.IsNullOrEmpty(propName) and
        // re-reads every binding on this source. Distinct from the revision-counter pattern this codebase
        // removed, which faked a value change to force a re-read; here the values genuinely have not changed,
        // only their presentation.
        OnPropertyChanged(propertyName: null);
    }

    // An unavailable or not-yet-read device renders in the idle tier rather than holding its last busy color.
    private double EffectiveUsagePercent => IsAvailable && UtilizationPercent is double percent ? percent : 0d;

    partial void OnUtilizationPercentChanged(double? value)
    {
        RefreshEffectiveUtilization();
        RefreshUsageTier();
    }

    partial void OnIsAvailableChanged(bool value)
    {
        RefreshEffectiveUtilization();
        RefreshUsageTier();
    }

    private void RefreshEffectiveUtilization() =>
        EffectiveUtilizationPercent = IsAvailable ? UtilizationPercent : null;

    // Fresh closure per call so the assignment never no-ops (delegates over the same method/target compare
    // equal); capturing a local gives each delegate a new target, so PropertyChanged fires and the axis rebinds.
    private Func<double, string> CreateUsageLabelFormatter()
    {
        // AxisTick, not AxisLabel: UsageHistory and VramHistory are converted in
        // DeviceCapabilitiesModel.ApplyComputeUsageHistoryAsync, so the tick is already in the user's unit and
        // FormatRatioAxisLabel would scale it a second time.
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatRatioAxisTick(value);
    }
}
