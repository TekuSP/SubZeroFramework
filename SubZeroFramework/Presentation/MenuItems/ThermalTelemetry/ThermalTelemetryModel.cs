using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using FrameworkDotnet.Enums;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

using SubZeroFramework.Controls.Thermal.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.ThermalTelemetry;

public partial class ThermalTelemetryModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ObservableCollection<ThermalSensorModel> _sensors = [];
    private readonly Dictionary<int, ThermalSensorModel> _sensorModelsByIndex = [];
    private readonly Dictionary<int, TelemetryPoint[]> _historyPoints = [];
    private readonly Dictionary<int, IDisposable> _historySubscriptions = [];
    private readonly Dictionary<ThermalSensorModel, PropertyChangedEventHandler> _sensorHandlers = [];
    private readonly Dictionary<int, ISeries> _thermalHistorySeriesBySensorIndex = [];
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly SynchronizationContext _synchronizationContext;

    public ThermalTelemetryModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        SynchronizationContext synchronizationContext)
    {
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _synchronizationContext = synchronizationContext;

        Sensors = new ReadOnlyObservableCollection<ThermalSensorModel>(_sensors);
        HistoryWindowOptions = PresentationDefaults.ThermalHistoryWindowLabels;
        UpdateThermalHistoryAxis([]);

        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(status => LastStatus = status)
            .DisposeWith(_subscriptions);

        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyTemperatureChanges)
            .DisposeWith(_subscriptions);
    }

    public ReadOnlyObservableCollection<ThermalSensorModel> Sensors { get; }

    public IReadOnlyList<string> HistoryWindowOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedHistoryWindow))]
    [NotifyPropertyChangedFor(nameof(SelectedHistoryWindowDisplay))]
    public partial int SelectedHistoryWindowIndex { get; set; }

    public TimeSpan SelectedHistoryWindow => PresentationDefaults.ThermalHistoryWindowValues[Math.Clamp(SelectedHistoryWindowIndex, 0, PresentationDefaults.ThermalHistoryWindowValues.Length - 1)];

    public string SelectedHistoryWindowDisplay => PresentationDefaults.ThermalHistoryWindowLabels[Math.Clamp(SelectedHistoryWindowIndex, 0, PresentationDefaults.ThermalHistoryWindowLabels.Length - 1)];

    [ObservableProperty]
    public partial ISeries[] ThermalHistorySeries { get; set; } = [];

    [ObservableProperty]
    public partial double[] ThermalHistorySeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? ThermalHistoryMinLimit { get; set; }

    [ObservableProperty]
    public partial double? ThermalHistoryMaxLimit { get; set; }

    public Func<double, string> ThermalLabelFormatter { get; } = static value => $"{value:N0}°C";

    public Func<DateTime, string> ThermalHistoryDateFormatter { get; } = ThermalSensorModel.Formatter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceModelDisplay))]
    [NotifyPropertyChangedFor(nameof(PlatformDisplay))]
    [NotifyPropertyChangedFor(nameof(DriverDisplay))]
    [NotifyPropertyChangedFor(nameof(EcBuildInfoDisplay))]
    [NotifyPropertyChangedFor(nameof(ServiceStateDisplay))]
    [NotifyPropertyChangedFor(nameof(TelemetryObservedDisplay))]
    [NotifyPropertyChangedFor(nameof(LastErrorDisplay))]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    public string DeviceModelDisplay => LastStatus?.DeviceModel ?? "Unknown device";

    public string PlatformDisplay => LastStatus?.PlatformFamily?.ToString() ?? "Unknown platform";

    public string DriverDisplay => LastStatus?.ActiveDriver?.ToString() ?? "Unknown driver";

    public string EcBuildInfoDisplay => string.IsNullOrWhiteSpace(LastStatus?.EcBuildInfo)
        ? "Unavailable"
        : LastStatus.EcBuildInfo!;

    public string ServiceStateDisplay
    {
        get
        {
            if (LastStatus is null || !LastStatus.IsGrpcActive)
            {
                return "Offline";
            }

            if (!LastStatus.IsLibraryAvailable)
            {
                return "FrameworkDotnet unavailable";
            }

            if (LastStatus.RequiresElevation)
            {
                return "Elevation required";
            }

            if (LastStatus.IsFrameworkDevice != true)
            {
                return "Unsupported device";
            }

            return "Live telemetry";
        }
    }

    public string TelemetryObservedDisplay => LastStatus is { LastTelemetryObservedAt: var observedAt } && observedAt != DateTimeOffset.MinValue
        ? observedAt.LocalDateTime.ToString("T")
        : "No samples yet";

    public string LastErrorDisplay => string.IsNullOrWhiteSpace(LastStatus?.LastError)
        ? "None"
        : LastStatus.LastError!;

    partial void OnSelectedHistoryWindowIndexChanged(int value)
    {
        if (value < 0 || value >= PresentationDefaults.ThermalHistoryWindowValues.Length)
        {
            return;
        }

        ResubscribeAllSensorHistory();
        UpdateThermalHistoryAxis();
    }

    private void ApplyTemperatureChanges(IChangeSet<TemperatureTelemetrySnapshot, int> set)
    {
        foreach (var change in set)
        {
            switch (change.Reason)
            {
                case ChangeReason.Add:
                case ChangeReason.Update:
                case ChangeReason.Refresh:
                    UpsertSensor(change.Current);
                    break;
                case ChangeReason.Remove:
                    RemoveSensor(change.Key);
                    break;
            }
        }
    }

    private void UpsertSensor(TemperatureTelemetrySnapshot snapshot)
    {
        if (_sensorModelsByIndex.TryGetValue(snapshot.SensorIndex, out var existingSensor))
        {
            existingSensor.Snapshot = snapshot;
            RefreshSensorHistory(existingSensor);
            return;
        }

        var sensor = new ThermalSensorModel
        {
            Snapshot = snapshot,
            IsSelected = snapshot.IsAvailable && _sensors.Count(existing => existing.IsSelected) < 4,
        };

        _sensorModelsByIndex[snapshot.SensorIndex] = sensor;
        AttachSensorHandler(sensor);
        _thermalHistorySeriesBySensorIndex[snapshot.SensorIndex] = CreateThermalHistorySeries(sensor);
        InsertSorted(_sensors, sensor, item => item.Snapshot.SensorIndex);
        EnsureHistorySubscription(snapshot.SensorIndex, SelectedHistoryWindow);
        RefreshSensorHistory(sensor);
        RefreshThermalHistoryChart();
    }

    private void RemoveSensor(int sensorIndex)
    {
        RemoveHistorySubscription(sensorIndex);

        _thermalHistorySeriesBySensorIndex.Remove(sensorIndex);

        if (_sensorModelsByIndex.Remove(sensorIndex, out var sensor))
        {
            DetachSensorHandler(sensor);
            _sensors.Remove(sensor);
        }

        RefreshThermalHistoryChart();
    }

    private void ResubscribeAllSensorHistory()
    {
        foreach (var sensorIndex in _sensorModelsByIndex.Keys.ToArray())
        {
            RemoveHistorySubscription(sensorIndex);
            EnsureHistorySubscription(sensorIndex, SelectedHistoryWindow);
        }
    }

    private void EnsureHistorySubscription(int sensorIndex, TimeSpan window)
    {
        if (_historySubscriptions.ContainsKey(sensorIndex))
        {
            return;
        }

        _historySubscriptions[sensorIndex] = SubscribeTemperatureHistory(sensorIndex, window);
    }

    private void RemoveHistorySubscription(int sensorIndex)
    {
        if (_historySubscriptions.Remove(sensorIndex, out var subscription))
        {
            subscription.Dispose();
        }

        _historyPoints.Remove(sensorIndex);

        if (_sensorModelsByIndex.TryGetValue(sensorIndex, out var sensor))
        {
            sensor.ClearTemperatureHistory();
        }
    }

    private IDisposable SubscribeTemperatureHistory(int sensorIndex, TimeSpan window) =>
        _temperatureTelemetryClient
            .WatchTemperatureHistory(sensorIndex, window)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(points =>
            {
                _historyPoints[sensorIndex] =
                [
                    .. points
                        .OrderBy(point => point.ObservedAt)
                        .ThenBy(point => point.SampleId)
                ];

                if (_sensorModelsByIndex.TryGetValue(sensorIndex, out var sensor))
                {
                    RefreshSensorHistory(sensor);
                }
            });

    private void RefreshSensorHistory(ThermalSensorModel sensor)
    {
        UpdateSensorHistory(sensor, _historyPoints.TryGetValue(sensor.Snapshot.SensorIndex, out var points) ? points : []);
    }

    private void AttachSensorHandler(ThermalSensorModel sensor)
    {
        if (_sensorHandlers.ContainsKey(sensor))
        {
            return;
        }

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(ThermalSensorModel.IsSelected))
            {
                RefreshThermalHistoryChart();
                return;
            }

            if (args.PropertyName == nameof(ThermalSensorModel.OverviewTemperatureHistory))
            {
                UpdateThermalHistoryAxis();
            }
        };

        sensor.PropertyChanged += handler;
        _sensorHandlers[sensor] = handler;
    }

    private void DetachSensorHandler(ThermalSensorModel sensor)
    {
        if (_sensorHandlers.Remove(sensor, out var handler))
        {
            sensor.PropertyChanged -= handler;
        }
    }

    private void DetachSensorHandlers()
    {
        foreach (var sensor in _sensorHandlers.Keys.ToArray())
        {
            DetachSensorHandler(sensor);
        }
    }

    private void RefreshThermalHistoryChart()
    {
        var selectedSensors = GetSelectedSensors();

        UpdateThermalHistoryAxis(selectedSensors);
        ThermalHistorySeries = [.. selectedSensors.Select(GetOrCreateThermalHistorySeries)];
    }

    private void UpdateThermalHistoryAxis()
    {
        UpdateThermalHistoryAxis(GetSelectedSensors());
    }

    private void UpdateThermalHistoryAxis(IEnumerable<ThermalSensorModel> selectedSensors)
    {
        var historyPoints = selectedSensors
            .SelectMany(sensor => sensor.OverviewTemperatureHistory)
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var (axisStart, axisEnd, separators) = TimeChartAxisHelper.BuildAxis(
            historyPoints,
            SelectedHistoryWindow,
            PresentationDefaults.StandardTelemetryHistorySeparatorStep);

        ThermalHistoryMinLimit = axisStart.Ticks;
        ThermalHistoryMaxLimit = axisEnd.Ticks;
        ThermalHistorySeparators = separators;
    }

    private ThermalSensorModel[] GetSelectedSensors()
    {
        return [.. Sensors.Where(sensor => sensor.IsSelected)];
    }

    private ISeries GetOrCreateThermalHistorySeries(ThermalSensorModel sensor)
    {
        if (_thermalHistorySeriesBySensorIndex.TryGetValue(sensor.Snapshot.SensorIndex, out var series))
        {
            return series;
        }

        series = CreateThermalHistorySeries(sensor);
        _thermalHistorySeriesBySensorIndex[sensor.Snapshot.SensorIndex] = series;
        return series;
    }

    private static ISeries CreateThermalHistorySeries(ThermalSensorModel sensor)
    {
        var strokeColor = SKColor.Parse(sensor.HistoryStrokeHex);

        return new LineSeries<DateTimePoint>
        {
            Values = sensor.OverviewTemperatureHistory,
            Name = sensor.DisplayName,
            Fill = null,
            GeometrySize = 6,
            GeometryFill = new SolidColorPaint(strokeColor),
            GeometryStroke = new SolidColorPaint(strokeColor, 2),
            LineSmoothness = 0.6,
            Stroke = new SolidColorPaint(strokeColor, 3),
        };
    }

    private static void UpdateSensorHistory(ThermalSensorModel sensor, IReadOnlyList<TelemetryPoint> points)
    {
        DateTimePoint[] overviewHistory = points.Count == 0
            ? []
            :
            [
                .. points.Select(point => new DateTimePoint(
                    point.ObservedAt.LocalDateTime,
                    point.NumericValue == 0d ? null : point.NumericValue))
            ];

        if (ShouldAppendCurrentGap(sensor.Snapshot))
        {
            overviewHistory = AppendNullGapPoint(overviewHistory, sensor.Snapshot.ObservedAt.LocalDateTime);
        }

        if (overviewHistory.Length == 0)
        {
            sensor.ClearTemperatureHistory();
            return;
        }

        sensor.UpdateTemperatureHistory(overviewHistory, overviewHistory);
    }

    private static bool ShouldAppendCurrentGap(TemperatureTelemetrySnapshot snapshot)
    {
        if (!snapshot.IsAvailable || snapshot.TemperatureCelsius is null)
        {
            return true;
        }

        return snapshot.TemperatureState is not null && snapshot.TemperatureState != FrameworkTemperatureState.Ok;
    }

    private static DateTimePoint[] AppendNullGapPoint(DateTimePoint[] history, DateTime observedAt)
    {
        if (history.Length == 0)
        {
            return [new DateTimePoint(observedAt, null)];
        }

        var lastPoint = history[^1];
        if (lastPoint.Value is null && observedAt <= lastPoint.DateTime)
        {
            return history;
        }

        var gapTime = observedAt <= lastPoint.DateTime
            ? lastPoint.DateTime.AddTicks(1)
            : observedAt;

        return [.. history, new DateTimePoint(gapTime, null)];
    }

    private static void InsertSorted<TModel>(ObservableCollection<TModel> target, TModel item, Func<TModel, int> keySelector)
    {
        var itemKey = keySelector(item);
        var insertIndex = 0;

        while (insertIndex < target.Count && keySelector(target[insertIndex]) < itemKey)
        {
            insertIndex++;
        }

        target.Insert(insertIndex, item);
    }

    public void Dispose()
    {
        DetachSensorHandlers();
        _subscriptions.Dispose();

        foreach (var subscription in _historySubscriptions.Values)
        {
            subscription.Dispose();
        }
    }
}
