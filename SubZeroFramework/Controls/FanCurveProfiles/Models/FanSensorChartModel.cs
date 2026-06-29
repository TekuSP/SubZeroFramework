using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SkiaSharp;

using SubZeroFramework.Models;
using SubZeroFramework.Presentation;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// Renders the Custom-curve "driving temperature" chart: one line per selected sensor plus the aggregated
/// driving line, a matching left-aligned legend, and the time axis. Owns only presentation state derived from
/// the selected sensors and their history; the page coordinator feeds it data via <see cref="Rebuild"/> (full
/// rebuild on selection/aggregation change) and <see cref="RefreshLiveData"/> (sampled live update).
/// </summary>
public partial class FanSensorChartModel : ObservableObject
{
    private static readonly SKColor DrivingTemperatureColor = new(80, 150, 255);

    private static readonly SKColor[] Palette =
    [
        new(138, 183, 232),
        new(232, 168, 124),
        new(168, 220, 158),
        new(220, 158, 220),
        new(244, 213, 134),
        new(158, 220, 220),
        new(232, 138, 138),
    ];

    private readonly IUnitFormattingService _unitFormattingService;
    private readonly Dictionary<int, ObservableCollection<DateTimePoint>> _pointsBySensorIndex = [];
    private readonly Dictionary<int, ISeries> _seriesBySensorIndex = [];
    private readonly ObservableCollection<DateTimePoint> _drivingPoints = [];
    private readonly ObservableCollection<SensorLegendItem> _legend = [];
    private LineSeries<DateTimePoint>? _drivingSeries;

    public FanSensorChartModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        SensorChartLegend = new ReadOnlyObservableCollection<SensorLegendItem>(_legend);
    }

    /// <summary>Custom left-aligned legend rows under the chart (swatch + name + current value).</summary>
    public ReadOnlyObservableCollection<SensorLegendItem> SensorChartLegend { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SensorChartVisibility))]
    public partial ISeries[] SensorChartSeries { get; set; } = [];

    [ObservableProperty]
    public partial double[] SensorChartSeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? SensorChartMinLimit { get; set; }

    [ObservableProperty]
    public partial double? SensorChartMaxLimit { get; set; }

    public Visibility SensorChartVisibility =>
        SensorChartSeries.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Func<DateTime, string> SensorChartDateFormatter { get; } = static dt => dt.ToString("HH:mm:ss");

    public Func<double, string> SensorChartTemperatureFormatter { get; } = static value => $"{value:0.#}°";

    /// <summary>Full rebuild: refreshes per-sensor and driving points, rebinds the series array, axis and legend.</summary>
    public void Rebuild(IReadOnlyList<SensorChipModel> selected, IReadOnlyDictionary<int, TelemetryPoint[]> historyBySensor, TemperatureAggregationMode aggregation)
    {
        foreach (var chip in selected)
        {
            UpdateSensorPoints(chip.SensorIndex, historyBySensor);
        }

        UpdateDrivingPoints(selected, historyBySensor, aggregation);

        var series = new List<ISeries>(selected.Count + 1);
        foreach (var chip in selected)
        {
            series.Add(GetOrCreateSensorSeries(chip));
        }
        if (selected.Count > 0)
        {
            series.Add(GetOrCreateDrivingSeries(aggregation));
        }

        SensorChartSeries = [.. series];
        UpdateAxis(selected);
        RebuildLegend(selected, aggregation);
    }

    /// <summary>Sampled live update: refreshes the point collections and axis without rebinding the series array.</summary>
    public void RefreshLiveData(IReadOnlyList<SensorChipModel> selected, IReadOnlyDictionary<int, TelemetryPoint[]> historyBySensor, TemperatureAggregationMode aggregation)
    {
        foreach (var chip in selected)
        {
            UpdateSensorPoints(chip.SensorIndex, historyBySensor);
        }

        UpdateDrivingPoints(selected, historyBySensor, aggregation);
        UpdateAxis(selected);
    }

    /// <summary>Drops a removed sensor's cached series and points.</summary>
    public void RemoveSensor(int sensorIndex)
    {
        _seriesBySensorIndex.Remove(sensorIndex);
        _pointsBySensorIndex.Remove(sensorIndex);
    }

    /// <summary>Updates a sensor's cached series name in place (e.g. when its display name changes live).</summary>
    public void UpdateSensorName(int sensorIndex, string name)
    {
        if (_seriesBySensorIndex.TryGetValue(sensorIndex, out var series))
        {
            series.Name = name;
        }
    }

    private void RebuildLegend(IReadOnlyList<SensorChipModel> selected, TemperatureAggregationMode aggregation)
    {
        _legend.Clear();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var chip in selected)
        {
            var c = Palette[Math.Abs(chip.SensorIndex) % Palette.Length];
            _legend.Add(new SensorLegendItem
            {
                Name = chip.DisplayName,
                Swatch = new SolidColorBrush(Windows.UI.Color.FromArgb(255, c.Red, c.Green, c.Blue)),
                ValueDisplay = chip.TemperatureDisplay,
            });
        }

        var drivingValue = _drivingPoints.Count > 0
            ? $"{_drivingPoints[^1].Value ?? 0d:0}°"
            : "—";
        _legend.Add(new SensorLegendItem
        {
            Name = $"Driving ({aggregation})",
            Swatch = new SolidColorBrush(Windows.UI.Color.FromArgb(255, DrivingTemperatureColor.Red, DrivingTemperatureColor.Green, DrivingTemperatureColor.Blue)),
            ValueDisplay = drivingValue,
        });
    }

    private void UpdateDrivingPoints(IReadOnlyList<SensorChipModel> selectedChips, IReadOnlyDictionary<int, TelemetryPoint[]> historyBySensor, TemperatureAggregationMode aggregation)
    {
        _drivingPoints.Clear();
        if (selectedChips.Count == 0)
        {
            return;
        }

        var perSensor = new List<TelemetryPoint[]>(selectedChips.Count);
        foreach (var chip in selectedChips)
        {
            if (historyBySensor.TryGetValue(chip.SensorIndex, out var points) && points.Length > 0)
            {
                perSensor.Add(points);
            }
        }
        if (perSensor.Count == 0)
        {
            return;
        }

        var timestampSet = new SortedSet<DateTimeOffset>();
        foreach (var series in perSensor)
        {
            foreach (var p in series)
            {
                timestampSet.Add(p.ObservedAt);
            }
        }

        foreach (var timestamp in timestampSet)
        {
            var readings = new List<double>(perSensor.Count);
            foreach (var s in perSensor)
            {
                if (TemperatureSeriesMath.FindNearestValue(s, timestamp) is double value)
                {
                    readings.Add(value);
                }
            }
            if (readings.Count == 0) continue;

            var aggregated = aggregation switch
            {
                TemperatureAggregationMode.Average => readings.Average(),
                TemperatureAggregationMode.Maximum => readings.Max(),
                TemperatureAggregationMode.Minimum => readings.Min(),
                TemperatureAggregationMode.Median => TemperatureSeriesMath.Median(readings),
                _ => readings.Average(),
            };

            _drivingPoints.Add(new DateTimePoint(
                timestamp.LocalDateTime,
                _unitFormattingService.ConvertTemperature(aggregated)));
        }
    }

    private LineSeries<DateTimePoint> GetOrCreateDrivingSeries(TemperatureAggregationMode aggregation)
    {
        _drivingSeries ??= new LineSeries<DateTimePoint>
        {
            Values = _drivingPoints,
            Fill = null,
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0.65,
            Stroke = new SolidColorPaint(DrivingTemperatureColor, 3),
        };

        _drivingSeries.Name = $"Driving ({aggregation})";
        return _drivingSeries;
    }

    private void UpdateSensorPoints(int sensorIndex, IReadOnlyDictionary<int, TelemetryPoint[]> historyBySensor)
    {
        if (!_pointsBySensorIndex.TryGetValue(sensorIndex, out var collection))
        {
            collection = [];
            _pointsBySensorIndex[sensorIndex] = collection;
        }

        collection.Clear();
        if (historyBySensor.TryGetValue(sensorIndex, out var points))
        {
            foreach (var point in points)
            {
                collection.Add(new DateTimePoint(
                    point.ObservedAt.LocalDateTime,
                    _unitFormattingService.ConvertTemperature(point.NumericValue)));
            }
        }
    }

    private ISeries GetOrCreateSensorSeries(SensorChipModel chip)
    {
        if (_seriesBySensorIndex.TryGetValue(chip.SensorIndex, out var existing))
        {
            existing.Name = chip.DisplayName;
            return existing;
        }

        var color = Palette[chip.SensorIndex % Palette.Length];
        var values = _pointsBySensorIndex.GetValueOrDefault(chip.SensorIndex)
            ?? (_pointsBySensorIndex[chip.SensorIndex] = []);

        var series = new LineSeries<DateTimePoint>
        {
            Values = values,
            Name = chip.DisplayName,
            Fill = null,
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0.65,
            Stroke = new SolidColorPaint(color, 2),
        };

        _seriesBySensorIndex[chip.SensorIndex] = series;
        return series;
    }

    private void UpdateAxis(IReadOnlyList<SensorChipModel> selectedChips)
    {
        if (selectedChips.Count == 0)
        {
            SensorChartMinLimit = null;
            SensorChartMaxLimit = null;
            SensorChartSeparators = [];
            return;
        }

        var timestamps = selectedChips
            .SelectMany(c => _pointsBySensorIndex.TryGetValue(c.SensorIndex, out var col)
                ? col.Select(p => p.DateTime)
                : [])
            .Concat(_drivingPoints.Select(p => p.DateTime))
            .OrderBy(t => t)
            .ToArray();

        var (axisStart, axisEnd, separators) = TimeChartAxisHelper.BuildAxis(
            timestamps,
            PresentationDefaults.RecentTelemetryHistoryWindow,
            PresentationDefaults.RecentTelemetrySeparatorStep);

        SensorChartMinLimit = axisStart.Ticks;
        SensorChartMaxLimit = axisEnd.Ticks;
        SensorChartSeparators = separators;
    }
}
