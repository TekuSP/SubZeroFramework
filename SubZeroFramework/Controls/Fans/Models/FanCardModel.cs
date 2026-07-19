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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevPerSecondHistory))]
    public partial DateTimePoint[] FanSpeedHistory { get; set; } = [];

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DrivingTemperatureHistoryAxisMaxLimit))]
    [NotifyPropertyChangedFor(nameof(TemperatureSparkline))]
    public partial DateTimePoint[] DrivingTemperatureHistory { get; set; } = [];

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public Func<double, string> DrivingTemperatureLabelFormatter => _unitFormattingService.FormatTemperatureAxisLabel;

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
    public partial string FanSpeedValueDisplay { get; private set; } = "--";

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
        ? $"{FanSpeedValueDisplay} {FanSpeedUnitSuffix}"
        : "Stopped";

    public string RowSubLine
    {
        get
        {
            if (FanState?.FanState == FrameworkFanState.Stalled)
            {
                return "no rotation";
            }

            return Snapshot.SpeedRpm > 0 ? $"⌀ {FanSpeedValueDisplay}" : string.Empty;
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
                    ? $"Fixed {duty:0}% duty"
                    : "Manual duty",
                FanControlMode.Max => "Commanded to full speed",
                _ => "Controller policy",
            };
        }
    }

    // Header sparkline series plotted against sample index (not time) on a single shared scale, so the line
    // always spreads across the width and updates every poll. rev/s = RPM ÷ 60 keeps it numerically close to °C.
    public double[] RevPerSecondHistory =>
        [.. FanSpeedHistory.Where(static p => p.Value.HasValue).Select(static p => p.Value!.Value / 60d)];

    public double[] TemperatureSparkline =>
        [.. DrivingTemperatureHistory.Where(static p => p.Value.HasValue).Select(static p => p.Value!.Value)];

    [ObservableProperty]
    public partial string OneMinuteAverageDisplay { get; private set; } = "--";

    [ObservableProperty]
    public partial string PeakDisplay { get; private set; } = "--";

    private string FormatHistoryStatistic(Func<IEnumerable<double>, double> selector)
    {
        var values = FanSpeedHistory.Where(static p => p.Value.HasValue).Select(static p => p.Value!.Value).ToArray();
        var converted = values.Length > 0 ? selector(values) : _unitFormattingService.ConvertFanSpeed(Snapshot.SpeedRpm);
        return converted.ToString("N0", CultureInfo.CurrentCulture);
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
        RefreshFanSpeedDisplays();
    }

    // Reassigns the fan-speed gauge/axis/value/stat displays from the current snapshot, capability, history,
    // and unit preference. Guarded until the snapshot is seated (the card is populated before it is shown).
    private void RefreshFanSpeedDisplays()
    {
        if (Snapshot is null)
        {
            return;
        }

        MaximumFanSpeedAxisLimit = _unitFormattingService.ConvertFanSpeed(MaximumFanSpeedRpm);
        var speed = Math.Clamp(_unitFormattingService.ConvertFanSpeed(Snapshot.SpeedRpm), 0d, MaximumFanSpeedAxisLimit);
        FanSpeedGaugeValues = [speed];
        FanSpeedRemainingGaugeValues = [Math.Max(0d, MaximumFanSpeedAxisLimit - speed)];
        FanSpeedValueDisplay = _unitFormattingService.FormatFanSpeedValue(Snapshot.SpeedRpm);
        FanSpeedUnitSuffix = _unitFormattingService.FanSpeedUnitSuffix;
        FanSpeedHistoryAxisMaxLimit = Math.Max(MaximumFanSpeedAxisLimit, GetMaximumObservedFanSpeed()) * FanSpeedHistoryAxisHeadroomMultiplier;
        OneMinuteAverageDisplay = FormatHistoryStatistic(static values => values.Average());
        PeakDisplay = FormatHistoryStatistic(static values => values.Max());
    }

    // Fresh closure per call so the assignment never no-ops (delegates over the same method/target compare
    // equal); capturing a local gives each a new target, so PropertyChanged fires and the axis rebinds.
    private Func<double, string> CreateFanSpeedLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatFanSpeedAxisLabel(value);
    }

    partial void OnFanSpeedHistoryChanged(DateTimePoint[] value)
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

    private double GetMaximumObservedFanSpeed()
    {
        var maximum = Snapshot is null ? 0d : _unitFormattingService.ConvertFanSpeed(Snapshot.SpeedRpm);

        foreach (var point in FanSpeedHistory)
        {
            if (point.Value is double value)
            {
                maximum = Math.Max(maximum, value);
            }
        }

        return maximum;
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
            _ => "Auto",
        };

        if (Capability?.SupportsThermalReporting == false)
        {
            DrivingTemperature = "n/a";
            UpdateOverrideStatePresentation();
            return;
        }

        if (ControlState.Mode != FanControlMode.CustomCurve)
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
            OverrideStateBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
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
                : AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
            return;
        }

        if (!FanState.IsAvailable)
        {
            StatusText = "Status: Unavailable";
            StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
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
                StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
                break;
            case FrameworkFanState.NotPresent:
                StatusText = "Status: Not Present";
                StatusBrush = AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor);
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
