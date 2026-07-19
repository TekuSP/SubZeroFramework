using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
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

    public FanCurveChartModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        CurveSeriesPoints = new ReadOnlyObservableCollection<ObservablePoint>(_curveSeriesPoints);
        AppliedCurveSeriesPoints = new ReadOnlyObservableCollection<ObservablePoint>(_appliedCurveSeriesPoints);
        CurveTemperatureLabelFormatter = CreateCurveTemperatureLabelFormatter();
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

    private static readonly SKColor DrivingMarkerColor = new(0x9B, 0x7B, 0xFF);

    // Created once and reused: SolidColorPaint wraps a native SKPaint, so allocating a new one on every
    // telemetry tick (the marker refreshes ~3×/s while the custom editor is open) churns native memory. The
    // dashed violet stroke is constant; only the marker's X position changes per tick.
    private static readonly SolidColorPaint DrivingMarkerPaint =
        new(DrivingMarkerColor, 1.5f) { PathEffect = new DashEffect([5f, 4f]) };

    /// <summary>
    /// Updates the predicted-duty readout + driving-temperature marker for the current draft and live driving
    /// temperature. Returns whether a prediction is shown (so the coordinator can drive its visibility flag);
    /// clears both when not editing a custom curve or no driving temperature is available.
    /// </summary>
    public bool RefreshPrediction(CustomCurveSnapshot? draft, double? drivingTempCelsius, bool isCustomMode)
    {
        if (!isCustomMode || draft is null || drivingTempCelsius is not double celsius || _curveSeriesPoints.Count == 0)
        {
            PredictedDutyText = null;
            CurveDrivingSections = [];
            return false;
        }

        var duty = draft.InterpolateDuty(celsius);

        // Curve points are canonical Celsius, but the readout follows the user's temperature unit so it matches
        // the (unit-aware) curve chart axis.
        PredictedDutyText = $"At {_unitFormattingService.FormatTemperature(celsius, decimals: 0)} this curve targets {duty:0}% duty.";
        CurveDrivingSections =
        [
            new RectangularSection
            {
                Xi = celsius,
                Xj = celsius,
                Stroke = DrivingMarkerPaint,
            },
        ];
        return true;
    }

    /// <summary>Rebuilds the editable draft series from the current curve points, anchoring at 0 °C / 130 °C.</summary>
    public void RebuildCurve(IEnumerable<CurvePointModel> curvePoints)
    {
        _curveSeriesPoints.Clear();
        var ordered = curvePoints.OrderBy(static p => p.TemperatureCelsius).ToArray();
        if (ordered.Length == 0)
        {
            _curveSeriesPoints.Add(new ObservablePoint(0d, 0d));
            _curveSeriesPoints.Add(new ObservablePoint(130d, 100d));
            return;
        }

        if (ordered[0].TemperatureCelsius > 0)
        {
            _curveSeriesPoints.Add(new ObservablePoint(0d, 0d));
        }
        foreach (var point in ordered)
        {
            _curveSeriesPoints.Add(new ObservablePoint(point.TemperatureCelsius, point.DutyPercent));
        }
        if (ordered[^1].TemperatureCelsius < 130)
        {
            _curveSeriesPoints.Add(new ObservablePoint(130d, ordered[^1].DutyPercent));
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

        var ordered = baseline.CurvePoints.OrderBy(static p => p.Temperature).ToArray();
        if (ordered[0].Temperature > 0)
        {
            _appliedCurveSeriesPoints.Add(new ObservablePoint(0d, 0d));
        }
        foreach (var (temperature, duty) in ordered)
        {
            _appliedCurveSeriesPoints.Add(new ObservablePoint(temperature, duty));
        }
        if (ordered[^1].Temperature < 130)
        {
            _appliedCurveSeriesPoints.Add(new ObservablePoint(130d, ordered[^1].Duty));
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
