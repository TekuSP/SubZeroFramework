using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;

using SkiaSharp;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// Renders the custom-curve editor chart: the editable draft line, the read-only "applied" overlay, the
/// theme paints, and the unit-aware axis labellers. Owns only presentation — the coordinator feeds it the
/// draft points / applied baseline and keeps dirty, prediction, and sensor concerns. Mirrors
/// <see cref="FanSensorChartModel"/>.
/// </summary>
public partial class FanCurveChartModel : ObservableObject
{
    // The curve chart's visible temperature window (FanCurveEditorView.xaml axis limits). The operating point
    // is clamped into it so a reading outside the window parks the marker at the edge instead of vanishing.
    private const double CurveChartMinTemperature = 10d;
    private const double CurveChartMaxTemperature = 125d;

    private readonly IUnitFormattingService _unitFormattingService;
    private readonly ObservableCollection<ObservablePoint> _curveSeriesPoints = [];
    private readonly ObservableCollection<ObservablePoint> _appliedCurveSeriesPoints = [];

    // Holds at most one point — where fan control actually is right now.
    private readonly ObservableCollection<ObservablePoint> _operatingPointValues = [];

    public FanCurveChartModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        CurveSeriesPoints = new ReadOnlyObservableCollection<ObservablePoint>(_curveSeriesPoints);
        AppliedCurveSeriesPoints = new ReadOnlyObservableCollection<ObservablePoint>(_appliedCurveSeriesPoints);
        CurveTemperatureLabelFormatter = CreateCurveTemperatureLabelFormatter();

        // Built ONCE and never reassigned: the series observe the point collections above, so rebuilding the
        // curve or the overlay flows through without touching this array.
        CurveChartSeries =
        [
            new LineSeries<ObservablePoint>
            {
                Values = _appliedCurveSeriesPoints,
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.2,
                Stroke = AppliedCurveStrokePaint,
                IsHoverable = false,
            },
            new LineSeries<ObservablePoint>
            {
                Values = _curveSeriesPoints,
                Fill = CurveAreaFillPaint,
                GeometryFill = CurveGeometryFillPaint,
                GeometrySize = 12,
                LineSmoothness = 0.2,
                Stroke = CurveStrokePaint,
            },
            // A small dot, not a big glyph: the marker has to say "here" without covering the curve it sits on.
            // The crosshair rules below carry the position; this just pins the intersection.
            new ScatterSeries<ObservablePoint, CircleGeometry>
            {
                Values = _operatingPointValues,
                GeometrySize = 11,
                Fill = OperatingPointPaint,
                Stroke = null,
                // Must never steal the pointer hit-test from the draggable curve points underneath it.
                IsHoverable = false,
                IsVisibleAtLegend = false,
                ZIndex = 10,
            },
        ];
    }

    /// <summary>The editable draft curve points (with 0 °C / 130 °C anchors), bound by the chart line series.</summary>
    public ReadOnlyObservableCollection<ObservablePoint> CurveSeriesPoints { get; }

    /// <summary>
    /// Points of the curve the service currently has applied, rendered as a faint read-only overlay so the
    /// user can compare their draft against what is actually running. Empty when there is no applied curve.
    /// </summary>
    public ReadOnlyObservableCollection<ObservablePoint> AppliedCurveSeriesPoints { get; }

    // Faint, semi-transparent line so the applied curve reads as a reference behind the editable draft.
    public SolidColorPaint AppliedCurveStrokePaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartSubtleAxisLabelColor.R,
        AppThemeBrushes.ChartSubtleAxisLabelColor.G,
        AppThemeBrushes.ChartSubtleAxisLabelColor.B,
        140), 2f);

    public SolidColorPaint CurveStrokePaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartAccentColor.R,
        AppThemeBrushes.ChartAccentColor.G,
        AppThemeBrushes.ChartAccentColor.B,
        AppThemeBrushes.ChartAccentColor.A), 2.5f);

    public SolidColorPaint CurveGeometryFillPaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartAccentColor.R,
        AppThemeBrushes.ChartAccentColor.G,
        AppThemeBrushes.ChartAccentColor.B,
        AppThemeBrushes.ChartAccentColor.A));

    // Faint translucent area under the staged curve.
    public SolidColorPaint CurveAreaFillPaint { get; } = new(new SKColor(0x00, 0x78, 0xD7, 0x2B));

    public SolidColorPaint CurveAxisLabelsPaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartSubtleAxisLabelColor.R,
        AppThemeBrushes.ChartSubtleAxisLabelColor.G,
        AppThemeBrushes.ChartSubtleAxisLabelColor.B,
        AppThemeBrushes.ChartSubtleAxisLabelColor.A));

    public SolidColorPaint CurveAxisSeparatorsPaint { get; } = new(new SKColor(
        AppThemeBrushes.ChartSeparatorColor.R,
        AppThemeBrushes.ChartSeparatorColor.G,
        AppThemeBrushes.ChartSeparatorColor.B,
        AppThemeBrushes.ChartSeparatorColor.A));

    // Curve points stay canonical Celsius (the service keys them by integer Celsius and the pixel math maps to
    // Celsius), but axis labels honor the user's temperature unit via the shared formatter. Stored and
    // re-assigned by RefreshUnitFormatting so the axis labeler rebinds and the curve chart relabels in place
    // when machine-wide display-unit preferences change.
    [ObservableProperty]
    public partial Func<double, string> CurveTemperatureLabelFormatter { get; private set; }

    public Func<double, string> CurveDutyLabelFormatter { get; } = static value => $"{value:0}%";

    // Violet dashed vertical marker drawn on the curve at the live driving temperature.
    [ObservableProperty]
    public partial IEnumerable<LiveChartsCore.Kernel.IChartElement> CurveDrivingSections { get; set; } = [];

    /// <summary>"At N° this curve targets M% duty." for the live driving temperature, or null when not predicting.</summary>
    [ObservableProperty]
    public partial string? PredictedDutyText { get; set; }

    // The operating point and its crosshair share one colour so they read as a single indicator. Green, not
    // the curve's blue and not an alarm red: the marker reports where the fan IS, which is not a fault.
    private static readonly SKColor DrivingMarkerColor = new(
        AppThemeBrushes.StatusSuccessColor.R,
        AppThemeBrushes.StatusSuccessColor.G,
        AppThemeBrushes.StatusSuccessColor.B);

    // Created once and reused: SolidColorPaint wraps a native SKPaint, so allocating a new one on every
    // telemetry tick (the marker refreshes ~3×/s while the custom editor is open) churns native memory. Only
    // the crosshair's position changes per tick, never its paint.
    private static readonly SolidColorPaint DrivingMarkerPaint =
        new(DrivingMarkerColor, 1.5f) { PathEffect = new DashEffect([5f, 4f]) };

    // Same reuse rule — one native SKPaint for the life of the editor.
    private static readonly SolidColorPaint OperatingPointPaint = new(DrivingMarkerColor);

    /// <summary>
    /// Applied-curve overlay, editable draft, and the live operating-point marker, in draw order. Built once
    /// in the constructor: the chart binds this array and the series watch the point collections themselves.
    /// </summary>
    public LiveChartsCore.ISeries[] CurveChartSeries { get; }

    /// <summary>
    /// Updates the predicted-duty readout + driving-temperature marker for the current draft and live driving
    /// temperature. Returns whether a prediction is shown (so the coordinator can drive its visibility flag);
    /// clears both when not editing a custom curve or no driving temperature is available.
    /// </summary>
    public bool RefreshPrediction(CustomCurveSnapshot? draft, double? drivingTempCelsius, double? appliedDutyPercent, bool isCustomMode)
    {
        if (!isCustomMode || draft is null || drivingTempCelsius is not double celsius || _curveSeriesPoints.Count == 0)
        {
            PredictedDutyText = null;
            CurveDrivingSections = [];

            // Hide the marker rather than leave it at a stale position — a marker that lingers asserts a duty
            // the fan is no longer running.
            if (_operatingPointValues.Count > 0)
            {
                _operatingPointValues.Clear();
            }

            return false;
        }

        var duty = draft.InterpolateDuty(celsius);

        // The marker reports the RUNNING fan, never the draft: it is only drawn when the service is actually
        // curve-driving this fan and has reported the duty it wrote. Falling back to the draft's own duty here
        // would have the marker assert a speed the fan is not running — e.g. while the draft says 0% but the
        // applied profile cannot read its sensors, so the fan is back under firmware control.
        var markerY = appliedDutyPercent;
        if (markerY is not double appliedDuty)
        {
            if (_operatingPointValues.Count > 0)
            {
                _operatingPointValues.Clear();
            }
        }
        else
        {
            var markerX = Math.Clamp(celsius, CurveChartMinTemperature, CurveChartMaxTemperature);
            var clampedDuty = FanCurveDomain.ClampDuty(appliedDuty);
            if (_operatingPointValues.Count == 1)
            {
                // Mutate in place: ObservablePoint raises its own property change, so the ~3 Hz refresh costs
                // no allocation and does not reset the collection.
                _operatingPointValues[0].X = markerX;
                _operatingPointValues[0].Y = clampedDuty;
            }
            else
            {
                _operatingPointValues.Clear();
                _operatingPointValues.Add(new ObservablePoint(markerX, clampedDuty));
            }
        }

        // Curve points are canonical Celsius, but the readout follows the user's temperature unit so it matches
        // the (unit-aware) curve chart axis.
        PredictedDutyText = $"At {_unitFormattingService.FormatTemperature(celsius, decimals: 0)} this curve targets {duty:0}% duty.";

        // Crosshair through the operating point: the vertical rule reads the driving temperature off the X
        // axis, the horizontal one reads the duty off the Y axis. Zero-width / zero-height sections are how
        // LiveCharts draws a bare rule.
        CurveDrivingSections =
        [
            new RectangularSection
            {
                Xi = celsius,
                Xj = celsius,
                Stroke = DrivingMarkerPaint,
            },
            .. _operatingPointValues.Count == 1
                ?
                [
                    new RectangularSection
                    {
                        Yi = _operatingPointValues[0].Y,
                        Yj = _operatingPointValues[0].Y,
                        Stroke = DrivingMarkerPaint,
                    },
                ]
                : Array.Empty<RectangularSection>(),
        ];
        return true;
    }

    /// <summary>
    /// Rebuilds the editable draft series from the current curve points, closing it with the min/max-speed
    /// anchors (<see cref="FanCurveDomain"/>). Both anchors sit outside the chart's axis window on purpose —
    /// they are not editable points — but they are what makes every curve idle when cold and reach full speed
    /// by the top of the domain, however early the user's own points stop.
    /// </summary>
    public void RebuildCurve(IEnumerable<CurvePointModel> curvePoints)
    {
        _curveSeriesPoints.Clear();
        foreach (var (temperature, duty) in FanCurveDomain.BuildAnchoredSeries(curvePoints.Select(static p => (p.TemperatureCelsius, p.DutyPercent))))
        {
            _curveSeriesPoints.Add(new ObservablePoint(temperature, duty));
        }
    }

    /// <summary>
    /// Renders the read-only applied-curve overlay for <paramref name="applied"/> (null / empty clears it).
    /// Returns whether an overlay is now shown, so the coordinator can drive its visibility flag.
    /// </summary>
    public bool SetAppliedOverlay(CustomCurveSnapshot? applied)
    {
        _appliedCurveSeriesPoints.Clear();
        if (applied is not { } baseline || baseline.CurvePoints.Length == 0)
        {
            return false;
        }

        // Anchored identically to the draft series, so the overlay and the edited curve are comparable.
        foreach (var (temperature, duty) in FanCurveDomain.BuildAnchoredSeries(baseline.CurvePoints))
        {
            _appliedCurveSeriesPoints.Add(new ObservablePoint(temperature, duty));
        }

        return true;
    }

    /// <summary>Relabels the temperature axis after a machine-wide display-unit change.</summary>
    public void RefreshUnitFormatting() => CurveTemperatureLabelFormatter = CreateCurveTemperatureLabelFormatter();

    // Builds a fresh closure per call so the assignment never no-ops: delegates wrapping the same method on
    // the same target compare equal, and the MVVM Toolkit setter skips equal values — capturing a local gives
    // each delegate a new closure target, so PropertyChanged fires and the axis rebinds its labeler.
    private Func<double, string> CreateCurveTemperatureLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatTemperatureAxisLabel(value);
    }
}
