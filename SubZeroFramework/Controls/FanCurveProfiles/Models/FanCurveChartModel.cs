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
    private readonly IUnitFormattingService _unitFormattingService;
    private readonly ObservableCollection<ObservablePoint> _curveSeriesPoints = [];
    private readonly ObservableCollection<ObservablePoint> _appliedCurveSeriesPoints = [];

    // Holds at most one point — where fan control actually is right now.
    private readonly ObservableCollection<ObservablePoint> _operatingPointValues = [];

    // The canonical points behind each series, retained so a display-unit change can replot without the
    // coordinator resupplying them. The series themselves live in display space.
    private (int Temperature, double Duty)[] _lastCurvePoints = [];
    private (int Temperature, double Duty)[] _lastAppliedPoints = [];

    public FanCurveChartModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        CurveSeriesPoints = new ReadOnlyObservableCollection<ObservablePoint>(_curveSeriesPoints);
        AppliedCurveSeriesPoints = new ReadOnlyObservableCollection<ObservablePoint>(_appliedCurveSeriesPoints);
        CurveTemperatureLabelFormatter = CreateCurveTemperatureLabelFormatter();
        CurveDutyLabelFormatter = CreateCurveDutyLabelFormatter();

        // Without this the axes bind to 0/0 until the first unit change and the chart opens blank.
        RefreshAxisWindows();

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

    // The duty axis plots canonical percent; only its LABELS follow the user's ratio unit, so the axis
    // domain stays 0–100 while a fraction preference relabels 50 as "0.5". Stored (fresh closure per call) so
    // PropertyChanged fires on a unit change and LiveCharts rebinds the labeler.
    [ObservableProperty]
    public partial Func<double, string> CurveDutyLabelFormatter { get; private set; }

    // ----- Axis window, in DISPLAY units -----
    //
    // This chart plots in display space: the series, both axis windows and the pointer coordinates all live
    // in the user's chosen units, and the labelers format an already-converted value. Bounds come from
    // FanCurveDomain so the visible window cannot drift from the band the editor clamps points into.
    //
    // Because the chart is EDITED, the pointer path must convert back — see ToCanonicalTemperature /
    // ToCanonicalDuty, which FanCurveEditorView calls on every press and drag. Those two are the whole
    // reason this is delicate: without them a point dragged to the tick reading 150 °F would be stored as
    // 150 °C.

    /// <summary>Left edge of the temperature axis, in the user's unit.</summary>
    [ObservableProperty]
    public partial double CurveTemperatureAxisMinLimit { get; private set; }

    /// <summary>Right edge of the temperature axis, in the user's unit.</summary>
    [ObservableProperty]
    public partial double CurveTemperatureAxisMaxLimit { get; private set; }

    /// <summary>
    /// Temperature tick spacing, in the user's unit.
    /// </summary>
    /// <remarks>
    /// Chosen as a round number IN THE DISPLAY UNIT rather than converted from a canonical step, which is
    /// the point of plotting in display space: 25 °F ticks read 50, 75, 100 instead of the 50, 68, 86 a
    /// converted 10 °C step produces.
    /// </remarks>
    [ObservableProperty]
    public partial double CurveTemperatureAxisMinStep { get; private set; }

    /// <summary>Bottom of the duty axis, in the user's unit.</summary>
    [ObservableProperty]
    public partial double CurveDutyAxisMinLimit { get; private set; }

    /// <summary>Top of the duty axis, in the user's unit.</summary>
    [ObservableProperty]
    public partial double CurveDutyAxisMaxLimit { get; private set; }

    /// <summary>Duty tick spacing, in the user's unit.</summary>
    [ObservableProperty]
    public partial double CurveDutyAxisMinStep { get; private set; }

    /// <summary>
    /// A round tick spacing for the temperature axis, per unit.
    /// </summary>
    /// <remarks>
    /// Hand-picked per unit rather than derived, because "round" is a property of how people read a scale,
    /// not something arithmetic produces: 10 °C, 25 °F and 10 K are each the natural stride for their own
    /// scale across the ~0–130 °C span this chart covers.
    /// </remarks>
    private double ResolveTemperatureStep() => _unitFormattingService.TemperatureUnitSuffix switch
    {
        "°F" => 25d,
        "°R" => 25d,
        _ => 10d,
    };

    /// <summary>A round tick spacing for the duty axis, per ratio unit.</summary>
    private double ResolveDutyStep() => _unitFormattingService.ConvertRatio(20d);

    /// <summary>Recomputes both axis windows from the canonical domain into the current display units.</summary>
    private void RefreshAxisWindows()
    {
        CurveTemperatureAxisMinLimit = _unitFormattingService.ConvertTemperature(FanCurveDomain.ChartMinTemperatureCelsius);
        CurveTemperatureAxisMaxLimit = _unitFormattingService.ConvertTemperature(FanCurveDomain.ChartMaxTemperatureCelsius);
        CurveTemperatureAxisMinStep = ResolveTemperatureStep();

        CurveDutyAxisMinLimit = _unitFormattingService.ConvertRatio(FanCurveDomain.ChartMinDutyPercent);
        CurveDutyAxisMaxLimit = _unitFormattingService.ConvertRatio(FanCurveDomain.ChartMaxDutyPercent);
        CurveDutyAxisMinStep = ResolveDutyStep();
    }

    // ----- Pointer coordinates: display space in, canonical out -----

    /// <summary>
    /// Converts a temperature read off the chart back to canonical Celsius, for storing an edited point.
    /// </summary>
    /// <remarks>
    /// The inverse of the axis conversion, and the reason a display-space editable chart is safe. Every
    /// pointer coordinate must pass through here before reaching FanCurveDomain.ClampTemperature.
    /// </remarks>
    public double ToCanonicalTemperature(double displayTemperature)
        => _unitFormattingService.ConvertTemperatureToCelsius(displayTemperature);

    /// <summary>Converts a duty read off the chart back to canonical percent.</summary>
    public double ToCanonicalDuty(double displayDuty)
        => _unitFormattingService.ConvertRatioToPercent(displayDuty);

    // No display-space hit-test radius helper here, deliberately: because the pointer is converted to
    // canonical the moment it is read, the grab radii stay canonical too and are compared against canonical
    // points. Converting them as well would double-apply the scale. If the hit test ever moves into display
    // space, the temperature radius needs ConvertTemperatureDelta, NOT ConvertTemperature — an absolute
    // conversion would turn a 4.5 °C grab radius into 40 °F and make points grabbable from half the chart.

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
            // Clamped in CANONICAL space against the canonical window, then converted — the marker has to
            // land in the same display space as the series it sits on.
            var markerX = _unitFormattingService.ConvertTemperature(
                Math.Clamp(celsius, FanCurveDomain.ChartMinTemperatureCelsius, FanCurveDomain.ChartMaxTemperatureCelsius));
            var clampedDuty = _unitFormattingService.ConvertRatio(FanCurveDomain.ClampDuty(appliedDuty));
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
        PredictedDutyText = $"At {_unitFormattingService.FormatTemperature(celsius, decimals: 0)} this curve targets {_unitFormattingService.FormatRatio(duty, decimals: 0)} duty.";

        // Crosshair through the operating point: the vertical rule reads the driving temperature off the X
        // axis, the horizontal one reads the duty off the Y axis. Zero-width / zero-height sections are how
        // LiveCharts draws a bare rule.
        CurveDrivingSections =
        [
            new RectangularSection
            {
                // Display space, like every other coordinate on this chart.
                Xi = _unitFormattingService.ConvertTemperature(celsius),
                Xj = _unitFormattingService.ConvertTemperature(celsius),
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
        // Kept so a display-unit change can replot the same curve without the coordinator resupplying it.
        _lastCurvePoints = [.. curvePoints.Select(static p => (p.TemperatureCelsius, p.DutyPercent))];
        RebuildCurveSeries();
    }

    /// <summary>Replots the draft curve from the retained canonical points into the current display units.</summary>
    private void RebuildCurveSeries()
    {
        _curveSeriesPoints.Clear();
        foreach (var (temperature, duty) in FanCurveDomain.BuildAnchoredSeries(_lastCurvePoints))
        {
            _curveSeriesPoints.Add(new ObservablePoint(
                _unitFormattingService.ConvertTemperature(temperature),
                _unitFormattingService.ConvertRatio(duty)));
        }
    }

    /// <summary>
    /// Renders the read-only applied-curve overlay for <paramref name="applied"/> (null / empty clears it).
    /// Returns whether an overlay is now shown, so the coordinator can drive its visibility flag.
    /// </summary>
    public bool SetAppliedOverlay(CustomCurveSnapshot? applied)
    {
        _lastAppliedPoints = applied is { } baseline && baseline.CurvePoints.Length > 0
            ? [.. baseline.CurvePoints]
            : [];

        RebuildAppliedSeries();
        return _lastAppliedPoints.Length > 0;
    }

    /// <summary>Replots the applied overlay from its retained canonical points into the current display units.</summary>
    private void RebuildAppliedSeries()
    {
        _appliedCurveSeriesPoints.Clear();
        if (_lastAppliedPoints.Length == 0)
        {
            return;
        }

        // Anchored identically to the draft series, so the overlay and the edited curve are comparable.
        foreach (var (temperature, duty) in FanCurveDomain.BuildAnchoredSeries(_lastAppliedPoints))
        {
            _appliedCurveSeriesPoints.Add(new ObservablePoint(
                _unitFormattingService.ConvertTemperature(temperature),
                _unitFormattingService.ConvertRatio(duty)));
        }
    }

    /// <summary>
    /// Re-renders the whole chart in the newly chosen display units.
    /// </summary>
    /// <remarks>
    /// Everything here plots in display space, so a unit change has to move the DATA, not just the labels:
    /// both series are replotted from their retained canonical points and both axis windows recomputed. The
    /// operating-point marker and its crosshair are left to the next RefreshPrediction — they are driven by
    /// a live reading that arrives every tick anyway.
    /// </remarks>
    public void RefreshUnitFormatting()
    {
        CurveTemperatureLabelFormatter = CreateCurveTemperatureLabelFormatter();
        CurveDutyLabelFormatter = CreateCurveDutyLabelFormatter();
        RefreshAxisWindows();
        RebuildCurveSeries();
        RebuildAppliedSeries();
    }

    // Builds a fresh closure per call so the assignment never no-ops: delegates wrapping the same method on
    // the same target compare equal, and the MVVM Toolkit setter skips equal values — capturing a local gives
    // each delegate a new closure target, so PropertyChanged fires and the axis rebinds its labeler.
    // Both format an ALREADY-CONVERTED axis value. Not FormatTemperatureAxisLabel / FormatRatioAxisLabel,
    // which convert from canonical and would scale a display-space tick a second time.
    private Func<double, string> CreateCurveTemperatureLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatTemperatureAxisTick(value);
    }

    private Func<double, string> CreateCurveDutyLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatRatioAxisTick(value);
    }
}
