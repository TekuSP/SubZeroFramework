using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;

using Material.Icons;

using Microsoft.UI.Xaml;

using SkiaSharp;

using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FanCardModel : ObservableObject
{
    private const double DefaultMaximumFanSpeedRpm = 7500d;
    private const double FanSpeedHistoryAxisHeadroomMultiplier = 1.1d;
    private readonly IUnitFormattingService _unitFormattingService;

    public FanCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        FanSpeedLabelFormatter = CreateFanSpeedLabelFormatter();
        DrivingTemperatureLabelFormatter = CreateDrivingTemperatureLabelFormatter();
        FanSpeedUnitSuffix = unitFormattingService.FanSpeedUnitSuffix;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationDisplay))]
    [NotifyPropertyChangedFor(nameof(SlotLabel))]
    [NotifyPropertyChangedFor(nameof(RowSpeedDisplay))]
    [NotifyPropertyChangedFor(nameof(RowSubLine))]
    [NotifyPropertyChangedFor(nameof(SpeedBandBrush))]
    [NotifyPropertyChangedFor(nameof(SpeedBandPaint))]
    [NotifyPropertyChangedFor(nameof(GaugeNominalValues))]
    [NotifyPropertyChangedFor(nameof(GaugeCautionValues))]
    [NotifyPropertyChangedFor(nameof(GaugeCriticalValues))]
    [NotifyPropertyChangedFor(nameof(GaugeRemainingValues))]
    public partial FanTelemetrySnapshot Snapshot { get; set; } = default!;

    // The fan-speed displays derive from the snapshot speed, the capability max, and the unit preference;
    // reassign them whenever any of those changes.
    partial void OnSnapshotChanged(FanTelemetrySnapshot value) => RefreshFanSpeedDisplays();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumFanSpeedRpm))]
    [NotifyPropertyChangedFor(nameof(GaugeNominalValues))]
    [NotifyPropertyChangedFor(nameof(GaugeCautionValues))]
    [NotifyPropertyChangedFor(nameof(GaugeCriticalValues))]
    [NotifyPropertyChangedFor(nameof(GaugeRemainingValues))]
    public partial FanCapabilityState? Capability { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureVisibility))]
    [NotifyPropertyChangedFor(nameof(HeaderContext))]
    public partial FanControlStateSnapshot? ControlState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(SelectedAccentBarVisibility))]
    [NotifyPropertyChangedFor(nameof(RowBorderBrush))]
    [NotifyPropertyChangedFor(nameof(RowBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(FanSpeedStrokePaint))]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureStrokePaint))]
    [NotifyPropertyChangedFor(nameof(HistoryXAxisLabelsPaint))]
    [NotifyPropertyChangedFor(nameof(HistoryXAxisSeparatorsPaint))]
    [NotifyPropertyChangedFor(nameof(FanSpeedYAxisLabelsPaint))]
    [NotifyPropertyChangedFor(nameof(FanSpeedYAxisSeparatorsPaint))]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureYAxisLabelsPaint))]
    public partial bool IsSelected { get; set; }

    public Brush CardBackgroundBrush => IsSelected
        ? AppThemeBrushes.Get("CardSelectedBackgroundBrush", AppThemeBrushes.CardSelectedBackgroundColor)
        : AppThemeBrushes.Get("CardBackgroundBrush", AppThemeBrushes.CardBackgroundColor);

    public SolidColorPaint FanSpeedStrokePaint => new(ToSkColor(IsSelected
        ? AppThemeBrushes.ChartPrimaryOnSelectedColor
        : AppThemeBrushes.ChartPrimaryColor), 2);

    public SolidColorPaint DrivingTemperatureStrokePaint => new(ToSkColor(IsSelected
        ? AppThemeBrushes.ChartErrorOnSelectedColor
        : AppThemeBrushes.ChartErrorColor), 2);

    public SolidColorPaint HistoryXAxisLabelsPaint => new(ToSkColor(IsSelected
        ? AppThemeBrushes.ChartAxisLabelOnSelectedColor
        : AppThemeBrushes.ChartSubtleAxisLabelColor));

    public SolidColorPaint HistoryXAxisSeparatorsPaint => new(ToSkColor(IsSelected
        ? AppThemeBrushes.ChartSeparatorOnSelectedColor
        : AppThemeBrushes.ChartSeparatorColor));

    public SolidColorPaint FanSpeedYAxisLabelsPaint => new(ToSkColor(IsSelected
        ? AppThemeBrushes.ChartAxisLabelOnSelectedColor
        : AppThemeBrushes.ChartPrimaryColor));

    public SolidColorPaint FanSpeedYAxisSeparatorsPaint => new(ToSkColor(IsSelected
        ? AppThemeBrushes.ChartSeparatorOnSelectedColor
        : AppThemeBrushes.ChartSeparatorColor));

    public SolidColorPaint DrivingTemperatureYAxisLabelsPaint => new(ToSkColor(IsSelected
        ? AppThemeBrushes.ChartErrorOnSelectedColor
        : AppThemeBrushes.ChartErrorColor));

    private static SKColor ToSkColor(Windows.UI.Color color) => new(color.R, color.G, color.B, color.A);

    [ObservableProperty]
    public partial ImmutableArray<TemperatureTelemetrySnapshot> DrivingSensors { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowSubLine))]
    [NotifyPropertyChangedFor(nameof(SpeedBandBrush))]
    [NotifyPropertyChangedFor(nameof(SpeedBandPaint))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    public partial FanStateSnapshot? FanState { get; set; }

    /// <summary>
    /// Recent fan speeds in CANONICAL RPM — the source of truth the page assigns. Everything else derived
    /// from history (the chart series, the header sparkline, the average/peak statistics, the axis headroom)
    /// is computed from this, so a display-unit change re-derives them all rather than reinterpreting numbers
    /// whose unit had silently changed underneath.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevPerSecondHistory))]
    public partial DateTimePoint[] FanSpeedHistoryRpm { get; set; } = [];

    /// <summary>
    /// The same history CONVERTED to the user's fan-speed unit, for the chart. A LiveCharts series plots the
    /// numbers it is given against an axis in the same space, so this one collection genuinely has to live in
    /// display units — unlike a text readout, which a converter can format at render time.
    /// </summary>
    [ObservableProperty]
    public partial DateTimePoint[] FanSpeedHistory { get; private set; } = [];

    [ObservableProperty]
    public partial double[] Separators { get; set; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Status: Checking";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusChipBackground))]
    public partial Brush StatusBrush { get; set; } = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);

    /// <summary>Tinted fill behind the status chip (status colour at low alpha) so OK/Stalled read as filled pills.</summary>
    public Brush StatusChipBackground => StatusBrush is SolidColorBrush brush
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, brush.Color.R, brush.Color.G, brush.Color.B))
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetModeDisplay))]
    public partial string TargetMode { get; set; } = "Auto";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureDisplay))]
    public partial string DrivingTemperature { get; set; } = "--";

    [ObservableProperty]
    public partial string OverrideStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush OverrideStateBrush { get; set; } = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

    [ObservableProperty]
    public partial MaterialIconKind OverrideStateIcon { get; set; } = MaterialIconKind.Information;

    [ObservableProperty]
    public partial Visibility OverrideStateVisibility { get; set; } = Visibility.Collapsed;

    /// <summary>
    /// Driving temperatures in CANONICAL Celsius — the source of truth the page assigns, mirroring
    /// FanSpeedHistoryRpm. The chart series and the header sparkline are both derived from it, so a
    /// display-unit change re-derives them instead of reinterpreting numbers whose unit had changed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemperatureSparkline))]
    public partial DateTimePoint[] DrivingTemperatureHistoryCelsius { get; set; } = [];

    /// <summary>
    /// The same history CONVERTED for the chart. A LiveCharts series plots against an axis in its own space,
    /// so this one collection has to live in display units — and the axis limits and labeler below match it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureHistoryAxisMaxLimit))]
    public partial DateTimePoint[] DrivingTemperatureHistory { get; private set; } = [];

    partial void OnDrivingTemperatureHistoryCelsiusChanged(DateTimePoint[] value) => RefreshDrivingTemperatureDisplays();

    /// <summary>Re-derives the display-space temperature series from the canonical one.</summary>
    private void RefreshDrivingTemperatureDisplays()
    {
        var converted = new DateTimePoint[DrivingTemperatureHistoryCelsius.Length];
        for (var index = 0; index < DrivingTemperatureHistoryCelsius.Length; index++)
        {
            var point = DrivingTemperatureHistoryCelsius[index];
            converted[index] = new DateTimePoint(
                point.DateTime,
                point.Value is double celsius ? _unitFormattingService.ConvertTemperature(celsius) : null);
        }

        DrivingTemperatureHistory = converted;
    }

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    /// <summary>
    /// Formats an ALREADY-CONVERTED axis value. Deliberately not FormatTemperatureAxisLabel, which expects
    /// canonical Celsius and would convert a second time — the classic display-space-series-plus-converting-
    /// labeler bug. Stored so a unit change rebinds it; see CreateDrivingTemperatureLabelFormatter.
    /// </summary>
    [ObservableProperty]
    public partial Func<double, string> DrivingTemperatureLabelFormatter { get; private set; }

    /// <summary>
    /// The temperature axis floor, in display units.
    /// </summary>
    /// <remarks>
    /// NOT a hardcoded 0, which is the trap this axis fell into: zero is unit-invariant for a fan speed
    /// (0 RPM = 0 rev/s) but not for a temperature — 0 °C is 32 °F and 273 K, so a literal 0 against a
    /// Fahrenheit series wastes the lower half of the chart, and against a Kelvin series squashes every
    /// reading into the top fifth of it.
    /// </remarks>
    public double DrivingTemperatureHistoryAxisMinCelsius => 0d;

    /// <summary>
    /// The fan-speed axis floor, in display units.
    /// </summary>
    /// <remarks>
    /// Converted rather than written as a literal 0 even though every fan-speed unit shares an origin
    /// (0 RPM = 0 rev/s = 0 rad/s), so nothing here depends on someone re-deriving that each time. The
    /// judgement call about which zeros are safe is what let the temperature axis ship broken.
    /// </remarks>
    public double FanSpeedHistoryAxisMinRpm => 0d;

    // ----- Header sparkline scale -----

    /// <summary>
    /// Floor of the shared header sparkline axis.
    /// </summary>
    /// <remarks>
    /// This axis is deliberately UNITLESS. It carries two different quantities — rev/s and canonical
    /// Celsius — purely so they sit on one comparable scale, so there is no unit to convert it into. Both
    /// series feeding it are canonical-derived for the same reason.
    /// </remarks>
    public double HeaderSparklineAxisMinLimit => 0d;

    /// <summary>
    /// Ceiling of the shared header sparkline axis.
    /// </summary>
    /// <remarks>
    /// FIXED on purpose: rev/s tops out near 117 at 7,000 RPM and canonical temperature at ~100, so 120
    /// covers every realistic peak. Letting it auto-range made the sparkline zoom in and out on every poll
    /// as the running maximum wobbled.
    /// </remarks>
    public double HeaderSparklineAxisMaxLimit => 120d;

    public double DrivingTemperatureHistoryAxisMaxLimit
    {
        get
        {
            var max = 0d;
            foreach (var point in DrivingTemperatureHistory)
            {
                if (point.Value is double value && value > max)
                {
                    max = value;
                }
            }

            // Always keep a reasonable headroom; floor at the display equivalent of 80 °C.
            var floor = _unitFormattingService.ConvertTemperature(80d);
            return Math.Max(floor, max * 1.1d);
        }
    }

    public double MaximumFanSpeedRpm => Capability is { MaximumSpeedRpm: > 0 } capability
        ? capability.MaximumSpeedRpm
        : DefaultMaximumFanSpeedRpm;

    // The fan-speed gauge/axis/value displays are stored and reassigned (RefreshFanSpeedDisplays) whenever
    // the snapshot, the capability, the history, or the unit preference changes; the setters raise
    // PropertyChanged only for values that actually changed.
    [ObservableProperty]
    public partial double MaximumFanSpeedAxisLimit { get; private set; }

    [ObservableProperty]
    public partial double FanSpeedHistoryAxisMaxLimit { get; private set; }

    [ObservableProperty]
    public partial double[] FanSpeedGaugeValues { get; private set; } = [0d];

    [ObservableProperty]
    public partial double[] FanSpeedRemainingGaugeValues { get; private set; } = [0d];

    [ObservableProperty]
    public partial string FanSpeedUnitSuffix { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Func<double, string> FanSpeedLabelFormatter { get; private set; }

    public string TargetModeDisplay => $"Mode: {TargetMode}";

    // ===== Redesign master-list row presentation =====

    /// <summary>Location label for the redesigned fan list (uses the service-provided display name).</summary>
    public string LocationDisplay => Snapshot.DisplayName;

    public string SlotLabel => $"Slot {Snapshot.FanIndex}";

    public string RowSpeedDisplay => Snapshot.SpeedRpm > 0
        ? $"{_unitFormattingService.FormatFanSpeedValue(Snapshot.SpeedRpm)} {FanSpeedUnitSuffix}"
        : "Stopped";

    public string RowSubLine
    {
        get
        {
            if (FanState?.FanState == FrameworkFanState.Stalled)
            {
                return "no rotation";
            }

            return Snapshot.SpeedRpm > 0 ? $"⌀ {_unitFormattingService.FormatFanSpeedValue(Snapshot.SpeedRpm)}" : string.Empty;
        }
    }

    /// <summary>True when this fan has uncommitted staged edits (drives the row's "Changes pending" pill).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangesPendingVisibility))]
    public partial bool IsStaged { get; set; }

    public Visibility ChangesPendingVisibility => IsStaged ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// True when this fan is a linked partner of another (the user added it to that fan's "Applies to" group).
    /// While linked it is controlled by its leader: the master-list row is disabled and the mode controls hidden
    /// until it is unlinked. Driven by client link intent (set by <c>FanLinkSectionModel</c>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRowSelectable))]
    [NotifyPropertyChangedFor(nameof(LinkedNoteVisibility))]
    [NotifyPropertyChangedFor(nameof(ModePillVisibility))]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    public partial bool IsLinkedPartner { get; set; }

    /// <summary>Dim a linked partner's row so it reads as controlled-by-its-leader (disabled).</summary>
    public double RowOpacity => IsLinkedPartner ? 0.5 : 1d;

    /// <summary>Display name of the fan this one is linked to (its leader), or null when not linked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkedNoteText))]
    public partial string? LinkedLeaderName { get; set; }

    /// <summary>The master-list row is selectable unless this fan is a linked partner (controlled by its leader).</summary>
    public bool IsRowSelectable => !IsLinkedPartner;

    public Visibility LinkedNoteVisibility => IsLinkedPartner ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Hide the per-fan mode pill while linked — the leader drives the mode.</summary>
    public Visibility ModePillVisibility => IsLinkedPartner ? Visibility.Collapsed : Visibility.Visible;

    public string LinkedNoteText => LinkedLeaderName is { } leader
        ? $"Linked to {leader} — unlink to control"
        : string.Empty;

    /// <summary>Short status word for the redesigned status chips (e.g. "OK", "Stalled").</summary>
    public string StatusLabel => FanState?.FanState switch
    {
        FrameworkFanState.Stalled => "Stalled",
        FrameworkFanState.NotPresent => "Not present",
        FrameworkFanState.Ok => "OK",
        _ => Snapshot.IsAvailable ? "OK" : "Unavailable",
    };

    /// <summary>Glyph inside the round status badge — a check when OK, a cross when stalled.</summary>
    public MaterialIconKind StatusIcon => FanState?.FanState == FrameworkFanState.Stalled
        ? MaterialIconKind.Close
        : MaterialIconKind.Check;

    private double SpeedFraction => MaximumFanSpeedRpm > 0
        ? Math.Clamp(Snapshot.SpeedRpm / MaximumFanSpeedRpm, 0d, 1d)
        : 0d;

    // Speed-band colours (design tokens): nominal accent, caution amber, critical red. Bright values so the
    // row ring + arc read clearly (the app's StatusErrorBrush is a dark fill, unsuitable for a gauge stroke).
    private static readonly SKColor BandNominalColor = new(0x00, 0x78, 0xD7);
    private static readonly SKColor BandCautionColor = new(0xC5, 0x99, 0x4E);
    private static readonly SKColor BandCriticalColor = new(0xD9, 0x70, 0x6A);

    private SKColor SpeedBandColor
    {
        get
        {
            if (FanState?.FanState == FrameworkFanState.Stalled)
            {
                return BandCriticalColor;
            }

            var fraction = SpeedFraction;
            return fraction >= 0.85d ? BandCriticalColor
                : fraction >= 0.6d ? BandCautionColor
                : BandNominalColor;
        }
    }

    /// <summary>Severity band colour for the row's speed ring (nominal &lt; 60% → caution &lt; 85% → critical).</summary>
    public Brush SpeedBandBrush
    {
        get
        {
            var c = SpeedBandColor;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
        }
    }

    /// <summary>Band-coloured value arc for the row's mini ring gauge.</summary>
    public SolidColorPaint SpeedBandPaint => new(SpeedBandColor);

    /// <summary>Faint track behind the row ring's value arc (gauge track ~#474b4b).</summary>
    public SolidColorPaint SpeedTrackPaint { get; } = new(new SKColor(0x47, 0x4B, 0x4B));

    // ===== Segmented (multi-band) gauge values, mirroring the ThermalTelemetry sensor gauges =====
    // The arc is filled through severity bands (nominal 0-60% → caution 60-85% → critical 85-100%) so the
    // gauge reads as colour segments, not a single solid arc. All values are percentages with MaxValue=100.
    private double SpeedPercent => SpeedFraction * 100d;

    private double GetSpeedSegment(double startInclusive, double endExclusive)
    {
        var value = SpeedPercent;
        return value <= startInclusive ? 0d : Math.Min(value, endExclusive) - startInclusive;
    }

    public double[] GaugeNominalValues => [GetSpeedSegment(0d, 60d)];

    public double[] GaugeCautionValues => [GetSpeedSegment(60d, 85d)];

    public double[] GaugeCriticalValues => [GetSpeedSegment(85d, 100d)];

    public double[] GaugeRemainingValues => [Math.Max(0d, 100d - SpeedPercent)];

    public SolidColorPaint GaugeNominalPaint { get; } = new(BandNominalColor);

    public SolidColorPaint GaugeCautionPaint { get; } = new(BandCautionColor);

    public SolidColorPaint GaugeCriticalPaint { get; } = new(BandCriticalColor);

    public Visibility SelectedAccentBarVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Brush RowBorderBrush => IsSelected
        ? AppThemeBrushes.Get("BrandPrimaryBrush", AppThemeBrushes.ChartAccentColor)
        : AppThemeBrushes.Get("CardBorderBrush", AppThemeBrushes.CardBackgroundColor);

    // ===== Redesign detail-header presentation =====

    /// <summary>Mode-specific context line for the detail header (matches the prototype copy).</summary>
    public string HeaderContext
    {
        get
        {
            if (ControlState is not { } state)
            {
                return string.Empty;
            }

            return state.Mode switch
            {
                FanControlMode.CustomCurve =>
                    $"Custom curve · driven by {state.DrivingSensorIndices.Length} sensor{(state.DrivingSensorIndices.Length == 1 ? string.Empty : "s")}",
                FanControlMode.Manual => state.LastDutyPercent is double duty
                    ? $"Fixed {_unitFormattingService.FormatRatio(duty, decimals: 0)} duty"
                    : "Manual duty",
                FanControlMode.Max => "Commanded to full speed",
                FanControlMode.Adaptive =>
                    $"Adaptive · holding {_unitFormattingService.FormatTemperature(state.AdaptiveSettings.TargetTemperatureCelsius, decimals: 0)}",
                _ => "Controller policy",
            };
        }
    }

    // Header sparkline series plotted against sample index (not time) on a single shared scale, so the line
    // always spreads across the width and updates every poll. rev/s = RPM ÷ 60 keeps it numerically close to
    // °C — which only holds off the CANONICAL history; off the converted one the divisor would be wrong for
    // every fan-speed unit except RPM.
    public double[] RevPerSecondHistory =>
        [.. FanSpeedHistoryRpm.Where(static p => p.Value.HasValue).Select(static p => p.Value!.Value / 60d)];

    // From the CANONICAL series, because it shares a fixed 0-120 axis with RevPerSecondHistory purely so the
    // two lines sit on one comparable scale. In Fahrenheit a display-space temperature would run to ~212 and
    // leave the window entirely; in Kelvin it never enters it.
    public double[] TemperatureSparkline =>
        [.. DrivingTemperatureHistoryCelsius.Where(static p => p.Value.HasValue).Select(static p => p.Value!.Value)];

    // History statistics in CANONICAL RPM, formatted by UnitFormatConverter (value-only — the unit is drawn
    // beside them from FanSpeedUnitSuffix). Computed over the canonical history so the average (or peak) is
    // of the real speeds rather than of already-rounded display numbers.
    [ObservableProperty]
    public partial double? OneMinuteAverageRpm { get; private set; }

    [ObservableProperty]
    public partial double? PeakRpm { get; private set; }

    private double ComputeHistoryStatistic(Func<IEnumerable<double>, double> selector)
    {
        var values = FanSpeedHistoryRpm.Where(static p => p.Value.HasValue).Select(static p => p.Value!.Value).ToArray();
        return values.Length > 0 ? selector(values) : Snapshot.SpeedRpm;
    }

    // Fading sparkline strokes for the header history (old → transparent, now → opaque).
    public LinearGradientPaint HeaderRevStrokePaint => new(
        [new SKColor(0, 120, 215, 18), new SKColor(0, 120, 215, 255)],
        new SKPoint(0, 0),
        new SKPoint(1, 0))
    {
        StrokeThickness = 2.5f,
    };

    public LinearGradientPaint HeaderTempStrokePaint => new(
        [new SKColor(217, 112, 106, 26), new SKColor(217, 112, 106, 255)],
        new SKPoint(0, 0),
        new SKPoint(1, 0))
    {
        StrokeThickness = 2.5f,
    };

    // Selected rows use a subtle accent wash (≈16% alpha) rather than a solid flood, per the redesign.
    public Brush RowBackgroundBrush => IsSelected
        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, 0, 120, 215))
        : AppThemeBrushes.Get("CardSecondaryBackgroundBrush", AppThemeBrushes.CardBackgroundColor);

    public string DrivingTemperatureDisplay => $"Driving Temp: {DrivingTemperature}";

    public Visibility DrivingTemperatureVisibility =>
        ControlState?.Mode == FanControlMode.CustomCurve ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshUnitFormatting()
    {
        UpdateControlStatePresentation();
        FanSpeedLabelFormatter = CreateFanSpeedLabelFormatter();
        DrivingTemperatureLabelFormatter = CreateDrivingTemperatureLabelFormatter();
        RefreshFanSpeedDisplays();
        RefreshDrivingTemperatureDisplays();

        // The canonical readings on this card are formatted by UnitFormatConverter at render time, so they
        // only need their bindings to run again — that is what the null property name asks for. It is also
        // what tells the dashboard's quick-control wrapper to rebuild its own composite text.
        OnPropertyChanged(propertyName: null);
    }

    // Reassigns the fan-speed gauge/axis/value/stat displays from the current snapshot, capability, history,
    // and unit preference. Guarded until the snapshot is seated (the card is populated before it is shown).
    private void RefreshFanSpeedDisplays()
    {
        if (Snapshot is null)
        {
            return;
        }

        // The chart series is the only collection that lives in display units, so it is re-derived from the
        // canonical history here — on a unit change as well as on new data.
        var convertedHistory = new DateTimePoint[FanSpeedHistoryRpm.Length];
        for (var index = 0; index < FanSpeedHistoryRpm.Length; index++)
        {
            var point = FanSpeedHistoryRpm[index];
            convertedHistory[index] = new DateTimePoint(
                point.DateTime,
                point.Value is double rpm ? _unitFormattingService.ConvertFanSpeed(rpm) : null);
        }

        FanSpeedHistory = convertedHistory;

        MaximumFanSpeedAxisLimit = _unitFormattingService.ConvertFanSpeed(MaximumFanSpeedRpm);
        var speed = Math.Clamp(_unitFormattingService.ConvertFanSpeed(Snapshot.SpeedRpm), 0d, MaximumFanSpeedAxisLimit);
        FanSpeedGaugeValues = [speed];
        FanSpeedRemainingGaugeValues = [Math.Max(0d, MaximumFanSpeedAxisLimit - speed)];
        FanSpeedUnitSuffix = _unitFormattingService.FanSpeedUnitSuffix;
        FanSpeedHistoryAxisMaxLimit = Math.Max(MaximumFanSpeedAxisLimit, GetMaximumObservedFanSpeed()) * FanSpeedHistoryAxisHeadroomMultiplier;
        OneMinuteAverageRpm = ComputeHistoryStatistic(static values => values.Average());
        PeakRpm = ComputeHistoryStatistic(static values => values.Max());
    }

    // Fresh closure per call so the assignment never no-ops (delegates over the same method/target compare
    // equal); capturing a local gives each a new target, so PropertyChanged fires and the axis rebinds.
    private Func<double, string> CreateFanSpeedLabelFormatter()
    {
        // Formats an ALREADY-CONVERTED axis value: FanSpeedHistory and both fan-speed axis limits live in
        // display units, so FormatFanSpeedAxisLabel — which converts from RPM — would scale them twice.
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatFanSpeedAxisTick(value);
    }

    private Func<double, string> CreateDrivingTemperatureLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatTemperatureAxisTick(value);
    }

    partial void OnFanSpeedHistoryRpmChanged(DateTimePoint[] value)
    {
        Separators = GetSeparators();
        RefreshFanSpeedDisplays();
    }

    partial void OnCapabilityChanged(FanCapabilityState? value)
    {
        UpdateCapabilityPresentation();
        RefreshFanSpeedDisplays();
    }

    partial void OnControlStateChanged(FanControlStateSnapshot? value)
    {
        UpdateControlStatePresentation();
    }

    partial void OnDrivingSensorsChanged(ImmutableArray<TemperatureTelemetrySnapshot> value)
    {
        UpdateControlStatePresentation();
    }

    partial void OnFanStateChanged(FanStateSnapshot? value)
    {
        UpdateFanStatePresentation();
    }

    private double[] GetSeparators()
    {
        var now = DateTime.Now;

        return TimeChartAxisHelper.BuildSeparators(
            now - PresentationDefaults.RecentTelemetryHistoryWindow,
            now,
            PresentationDefaults.RecentTelemetrySeparatorStep);
    }

    /// <summary>Highest speed seen (live or in history), in the user's fan-speed unit — it bounds a display-space axis.</summary>
    private double GetMaximumObservedFanSpeed()
    {
        var maximumRpm = Snapshot?.SpeedRpm ?? 0d;

        foreach (var point in FanSpeedHistoryRpm)
        {
            if (point.Value is double value)
            {
                maximumRpm = Math.Max(maximumRpm, value);
            }
        }

        return _unitFormattingService.ConvertFanSpeed(maximumRpm);
    }

    private void UpdateCapabilityPresentation()
    {
        UpdateControlStatePresentation();
        UpdateFanStatePresentation();
    }

    private void UpdateControlStatePresentation()
    {
        if (ControlState is null || !ControlState.IsAvailable)
        {
            TargetMode = "Auto";
            DrivingTemperature = Capability?.SupportsThermalReporting == false ? "n/a" : _unitFormattingService.FormatTemperature(null);
            UpdateOverrideStatePresentation();
            return;
        }

        TargetMode = ControlState.Mode switch
        {
            FanControlMode.Auto => "Auto",
            FanControlMode.Manual => "Manual",
            FanControlMode.CustomCurve => "Curve",
            FanControlMode.Max => "Max",
            FanControlMode.Adaptive => "Adaptive",
            _ => "Auto",
        };

        if (Capability?.SupportsThermalReporting == false)
        {
            DrivingTemperature = "n/a";
            UpdateOverrideStatePresentation();
            return;
        }

        // Adaptive drives by sensors exactly as a curve does, so its aggregated temperature shows too.
        if (ControlState.Mode is not (FanControlMode.CustomCurve or FanControlMode.Adaptive))
        {
            DrivingTemperature = _unitFormattingService.FormatTemperature(null);
            UpdateOverrideStatePresentation();
            return;
        }

        DrivingTemperature = FormatDrivingTemperature(
            ControlState.DrivingTemperatureAggregation,
            ControlState.DrivingSensorIndices,
            DrivingSensors);
        UpdateOverrideStatePresentation();
    }

    private void UpdateOverrideStatePresentation()
    {
        if (ControlState?.LastAutoRestoreAttemptFailed == true)
        {
            OverrideStateText = "Auto restore failed";
            OverrideStateIcon = MaterialIconKind.AlertCircle;
            OverrideStateBrush = AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
            OverrideStateVisibility = Visibility.Visible;
            return;
        }

        var secondaryBrush = AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.TextSecondaryColor);

        if (ControlState?.Mode == FanControlMode.Max)
        {
            OverrideStateText = "Max speed";
            OverrideStateIcon = MaterialIconKind.Speedometer;
            OverrideStateBrush = secondaryBrush;
            OverrideStateVisibility = Visibility.Visible;
            return;
        }

        if (ControlState?.HasActiveOverride == true)
        {
            if (ControlState.Mode == FanControlMode.CustomCurve)
            {
                OverrideStateText = ControlState.LastDutyPercent is double curveDutyPercent
                    ? $"Curve: {_unitFormattingService.FormatRatio(curveDutyPercent, decimals: 0)}"
                    : "Curve override active";
                OverrideStateIcon = MaterialIconKind.ChartBellCurve;
            }
            else if (ControlState.Mode == FanControlMode.Manual && ControlState.LastDutyPercent is double dutyPercent)
            {
                OverrideStateText = $"Manual: {_unitFormattingService.FormatRatio(dutyPercent, decimals: 0)}";
                OverrideStateIcon = MaterialIconKind.Tune;
            }
            else
            {
                OverrideStateText = "Manual override active";
                OverrideStateIcon = MaterialIconKind.Tune;
            }

            OverrideStateBrush = secondaryBrush;
            OverrideStateVisibility = Visibility.Visible;
            return;
        }

        OverrideStateText = string.Empty;
        OverrideStateBrush = secondaryBrush;
        OverrideStateVisibility = Visibility.Collapsed;
    }

    private void UpdateFanStatePresentation()
    {
        if (FanState is null)
        {
            StatusText = Snapshot.IsAvailable ? "Status: Checking" : "Status: Unavailable";
            StatusBrush = Snapshot.IsAvailable
                ? AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor)
                : AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
            return;
        }

        if (!FanState.IsAvailable)
        {
            StatusText = "Status: Unavailable";
            StatusBrush = AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
            return;
        }

        switch (FanState.FanState)
        {
            case FrameworkFanState.Ok:
                StatusText = "Status: OK";
                StatusBrush = AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor);
                break;
            case FrameworkFanState.Stalled:
                StatusText = "Status: Stalled";
                StatusBrush = AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
                break;
            case FrameworkFanState.NotPresent:
                StatusText = "Status: Not Present";
                StatusBrush = AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor);
                break;
            default:
                StatusText = "Status: Unknown";
                StatusBrush = AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);
                break;
        }
    }

    private string FormatDrivingTemperature(TemperatureAggregationMode aggregationMode, ImmutableArray<int> sensorIndices, ImmutableArray<TemperatureTelemetrySnapshot> drivingSensors)
    {
        var aggregationLabel = aggregationMode switch
        {
            TemperatureAggregationMode.Average => "avg",
            TemperatureAggregationMode.Median => "median",
            TemperatureAggregationMode.Maximum => "max",
            TemperatureAggregationMode.Minimum => "min",
            _ => "avg",
        };

        var temperatures = drivingSensors
            .Where(sensor => sensor.IsAvailable
                && sensor.TemperatureCelsius is not null
                && (sensor.TemperatureState is null || sensor.TemperatureState == FrameworkTemperatureState.Ok))
            .Select(sensor => sensor.TemperatureCelsius!.Value)
            .OrderBy(value => value)
            .ToArray();

        var temperatureDisplay = temperatures.Length == 0
            ? _unitFormattingService.FormatTemperature(null)
            : _unitFormattingService.FormatTemperature(ComputeAggregateTemperature(aggregationMode, temperatures), decimals: 0);

        if (sensorIndices.IsDefaultOrEmpty)
        {
            return $"{temperatureDisplay} {aggregationLabel}";
        }

        return $"{temperatureDisplay} {aggregationLabel} [{string.Join(",", sensorIndices)}]";
    }

    private static double ComputeAggregateTemperature(TemperatureAggregationMode aggregationMode, double[] orderedTemperatures)
    {
        return aggregationMode switch
        {
            TemperatureAggregationMode.Average => orderedTemperatures.Average(),
            TemperatureAggregationMode.Median => ComputeMedianTemperature(orderedTemperatures),
            TemperatureAggregationMode.Maximum => orderedTemperatures[^1],
            TemperatureAggregationMode.Minimum => orderedTemperatures[0],
            _ => orderedTemperatures.Average(),
        };
    }

    private static double ComputeMedianTemperature(double[] orderedTemperatures)
    {
        var midpoint = orderedTemperatures.Length / 2;

        return orderedTemperatures.Length % 2 == 0
            ? (orderedTemperatures[midpoint - 1] + orderedTemperatures[midpoint]) / 2d
            : orderedTemperatures[midpoint];
    }

    public static string Formatter(DateTime date)
    {
        var secsAgo = (DateTime.Now - date).TotalSeconds;

        return secsAgo < 1
            ? "now"
            : $"{secsAgo:N0}s";
    }
}
