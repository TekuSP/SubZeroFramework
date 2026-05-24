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

using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Controls.Power.Models;
using SubZeroFramework.Controls.Thermal.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Presentation.Units;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class DashboardModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ObservableCollection<FanCardModel> _fans = [];
    private readonly ObservableCollection<ThermalSensorModel> _thermalSensors = [];
    private readonly ObservableCollection<PowerCardModel> _batteries = [];
    private readonly Dictionary<int, FanCardModel> _fanCardsByIndex = [];
    private readonly Dictionary<int, FanCapabilityState> _fanCapabilities = [];
    private readonly Dictionary<int, FanControlStateSnapshot> _fanControlStates = [];
    private readonly Dictionary<int, FanStateSnapshot> _fanStates = [];
    private readonly Dictionary<int, TemperatureTelemetrySnapshot> _temperatureSnapshots = [];
    private readonly Dictionary<int, ThermalSensorModel> _thermalSensorsByIndex = [];
    private readonly Dictionary<int, FanTelemetrySeriesPoint[]> _fanHistoryPoints = [];
    private readonly Dictionary<int, TelemetryPoint[]> _temperatureHistoryPoints = [];
    private readonly Dictionary<int, PowerCardModel> _batteryCardsByIndex = [];
    private readonly Dictionary<(int Index, TelemetryMetric Metric), TelemetryPoint[]> _batteryHistoryPoints = [];
    private readonly IFanCapabilityClient _fanCapabilityClient;
    private readonly IFanControlStateClient _fanControlStateClient;
    private readonly IFanStateClient _fanStateClient;
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly IBatteryTelemetryClient _batteryTelemetryClient;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly IUnitFormattingService _unitFormattingService;
    private static readonly TelemetryMetric[] BatteryHistoryMetrics =
    [
        TelemetryMetric.BatteryChargePercent,
        TelemetryMetric.BatteryPresentRateAmperes,
        TelemetryMetric.BatteryPresentVoltageVolts,
    ];

    private readonly Dictionary<int, IDisposable> _fanHistorySubscriptions = [];
    private readonly Dictionary<int, IDisposable> _temperatureHistorySubscriptions = [];
    private readonly Dictionary<(int Index, TelemetryMetric Metric), IDisposable> _batteryHistorySubscriptions = [];
    private readonly Dictionary<ThermalSensorModel, PropertyChangedEventHandler> _thermalSensorHandlers = [];
    private readonly Dictionary<int, ISeries> _thermalHistorySeriesBySensorIndex = [];

    public DashboardModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkTelemetryClient frameworkTelemetryClient,
        IFanCapabilityClient fanCapabilityClient,
        IFanControlStateClient fanControlStateClient,
        IFanStateClient fanStateClient,
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        IUserUnitPreferencesClient userUnitPreferencesClient,
        IUnitFormattingService unitFormattingService,
        SynchronizationContext synchronizationContext)
    {
        _fanCapabilityClient = fanCapabilityClient;
        _fanControlStateClient = fanControlStateClient;
        _fanStateClient = fanStateClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _batteryTelemetryClient = batteryTelemetryClient;
        _unitFormattingService = unitFormattingService;
        _synchronizationContext = synchronizationContext;

        Fans = new ReadOnlyObservableCollection<FanCardModel>(_fans);
        VisibleFans = Fans;
        ThermalSensors = new ReadOnlyObservableCollection<ThermalSensorModel>(_thermalSensors);
        VisibleThermalSensors = ThermalSensors;
        Batteries = new ReadOnlyObservableCollection<PowerCardModel>(_batteries);
        UpdateThermalHistoryAxis([]);

        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(status => LastStatus = status)
            .DisposeWith(_subscriptions);

        _fanCapabilityClient
            .WatchFanCapabilities()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    _fanCardsByIndex.TryGetValue(change.Key, out var fan);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanCapabilities.Remove(change.Key);
                        if (fan is not null)
                        {
                            fan.Capability = null;
                        }

                        continue;
                    }

                    _fanCapabilities[change.Key] = change.Current;
                    if (fan is not null)
                    {
                        fan.Capability = change.Current;
                    }
                }
            })
            .DisposeWith(_subscriptions);

        _fanControlStateClient
            .WatchFanControlStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    _fanCardsByIndex.TryGetValue(change.Key, out var fan);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanControlStates.Remove(change.Key);
                        if (fan is not null)
                        {
                            fan.ControlState = null;
                            fan.DrivingSensors = [];
                        }

                        continue;
                    }

                    _fanControlStates[change.Key] = change.Current;
                    if (fan is not null)
                    {
                        fan.ControlState = change.Current;
                        UpdateDrivingSensors(fan);
                    }
                }
            })
            .DisposeWith(_subscriptions);

        _fanStateClient
            .WatchFanStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    _fanCardsByIndex.TryGetValue(change.Key, out var fan);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanStates.Remove(change.Key);
                        if (fan is not null)
                        {
                            fan.FanState = null;
                        }

                        continue;
                    }

                    _fanStates[change.Key] = change.Current;
                    if (fan is not null)
                    {
                        fan.FanState = change.Current;
                    }
                }
            })
            .DisposeWith(_subscriptions);

        _fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        if (_fanCardsByIndex.TryGetValue(change.Key, out var existingFan))
                        {
                            existingFan.Snapshot = change.Current;
                            continue;
                        }

                        var fan = new FanCardModel(_unitFormattingService)
                        {
                            Snapshot = change.Current,
                            Capability = _fanCapabilities.GetValueOrDefault(change.Current.FanIndex),
                            ControlState = _fanControlStates.GetValueOrDefault(change.Current.FanIndex),
                            DrivingSensors = GetDrivingSensors(_fanControlStates.GetValueOrDefault(change.Current.FanIndex)),
                            FanState = _fanStates.GetValueOrDefault(change.Current.FanIndex),
                        };

                        _fanCardsByIndex[change.Key] = fan;
                        InsertSorted(_fans, fan, card => card.Snapshot.FanIndex);
                        EnsureFanHistorySubscription(change.Key, PresentationDefaults.RecentTelemetryHistoryWindow);
                        continue;
                    }

                    if (change.Reason == ChangeReason.Update || change.Reason == ChangeReason.Refresh)
                    {
                        if (_fanCardsByIndex.TryGetValue(change.Current.FanIndex, out var fan))
                        {
                            fan.Snapshot = change.Current;
                        }

                        continue;
                    }

                    if (change.Reason == ChangeReason.Remove)
                    {
                        RemoveFanCard(change.Key);
                    }
                }
            })
            .DisposeWith(_subscriptions);

        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                var drivingSensorsChanged = false;

                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        _temperatureSnapshots[change.Key] = change.Current;

                        if (_thermalSensorsByIndex.TryGetValue(change.Key, out var existingSensor))
                        {
                            existingSensor.Snapshot = change.Current;
                            RefreshThermalSensorHistory(existingSensor);
                            drivingSensorsChanged = true;
                            continue;
                        }

                        var thermalSensor = new ThermalSensorModel(_unitFormattingService)
                        {
                            Snapshot = change.Current,
                            IsSelected = ShouldShowThermalSensorByDefault(change.Current)
                                && _thermalSensors.Count(existingSensor => existingSensor.IsSelected) < 4,
                        };

                        _thermalSensorsByIndex[change.Key] = thermalSensor;
                        AttachThermalSensorHandler(thermalSensor);
                        _thermalHistorySeriesBySensorIndex[change.Key] = CreateThermalHistorySeries(thermalSensor);
                        InsertSorted(_thermalSensors, thermalSensor, sensor => sensor.Snapshot.SensorIndex);
                        EnsureTemperatureHistorySubscription(change.Key, TelemetryHistoryLimits.MaximumHistoryWindow);
                        RefreshThermalSensorHistory(thermalSensor);
                        RefreshThermalHistoryChart();
                        drivingSensorsChanged = true;
                        continue;
                    }

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _temperatureSnapshots.Remove(change.Key);
                        RemoveTemperatureSensor(change.Key);
                        drivingSensorsChanged = true;
                        continue;
                    }

                    _temperatureSnapshots[change.Key] = change.Current;
                    if (_thermalSensorsByIndex.TryGetValue(change.Key, out var sensor))
                    {
                        sensor.Snapshot = change.Current;
                        RefreshThermalSensorHistory(sensor);
                    }

                    drivingSensorsChanged = true;
                }

                if (drivingSensorsChanged)
                {
                    RefreshDrivingSensors();
                }
            })
            .DisposeWith(_subscriptions);

        _batteryTelemetryClient
            .WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        if (_batteryCardsByIndex.TryGetValue(change.Key, out var existingBattery))
                        {
                            existingBattery.BatterySnapshot = change.Current;
                            RefreshBatterySensorHistories(existingBattery);
                            continue;
                        }

                        var batteryCard = new PowerCardModel(_unitFormattingService)
                        {
                            BatterySnapshot = change.Current
                        };

                        _batteryCardsByIndex[change.Key] = batteryCard;
                        InsertSorted(_batteries, batteryCard, card => card.BatterySnapshot.BatteryIndex);
                        EnsureBatteryHistorySubscriptions(change.Key, TelemetryHistoryLimits.MaximumHistoryWindow);
                        RefreshBatterySensorHistories(batteryCard);
                        continue;
                    }

                    if (change.Reason == ChangeReason.Remove)
                    {
                        RemoveBatteryCard(change.Key);
                        continue;
                    }

                    if (_batteryCardsByIndex.TryGetValue(change.Key, out var battery))
                    {
                        battery.BatterySnapshot = change.Current;
                        RefreshBatterySensorHistories(battery);
                    }
                }
            })
            .DisposeWith(_subscriptions);

        userUnitPreferencesClient
            .WatchPreferences()
            .ObserveOn(_synchronizationContext)
            .Select(_ => Observable.FromAsync(RefreshUnitFormattingAsync))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    [ObservableProperty]
    public partial ISeries[] ThermalHistorySeries { get; set; } = [];

    [ObservableProperty]
    public partial double[] ThermalHistorySeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? ThermalHistoryMinLimit { get; set; }

    [ObservableProperty]
    public partial double? ThermalHistoryMaxLimit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThermalLabelFormatter))]
    private partial int UnitFormattingRevision { get; set; }

    public Func<double, string> ThermalLabelFormatter => _unitFormattingService.FormatTemperatureAxisLabel;

    public Func<DateTime, string> ThermalHistoryDateFormatter { get; } = ThermalSensorModel.Formatter;

    public ReadOnlyObservableCollection<FanCardModel> Fans { get; }

    public ReadOnlyObservableCollection<FanCardModel> VisibleFans { get; }

    public ReadOnlyObservableCollection<ThermalSensorModel> ThermalSensors { get; }

    public ReadOnlyObservableCollection<ThermalSensorModel> VisibleThermalSensors { get; }

    public ReadOnlyObservableCollection<PowerCardModel> Batteries { get; }

    private void UpdateDrivingSensors(FanCardModel fan)
    {
        fan.DrivingSensors = GetDrivingSensors(fan.ControlState);
    }

    private void RefreshDrivingSensors()
    {
        foreach (var fan in _fans)
        {
            UpdateDrivingSensors(fan);
        }
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

    private void EnsureFanHistorySubscription(int fanIndex, TimeSpan range)
    {
        if (!_fanHistorySubscriptions.ContainsKey(fanIndex))
        {
            _fanHistorySubscriptions.Add(fanIndex, SubscribeFanHistory(fanIndex, range));
        }
    }

    private void EnsureTemperatureHistorySubscription(int sensorIndex, TimeSpan range)
    {
        if (!_temperatureHistorySubscriptions.ContainsKey(sensorIndex))
        {
            _temperatureHistorySubscriptions.Add(sensorIndex, SubscribeTemperatureHistory(sensorIndex, range));
        }
    }

    private void EnsureBatteryHistorySubscriptions(int batteryIndex, TimeSpan range)
    {
        foreach (var metric in BatteryHistoryMetrics)
        {
            EnsureBatteryHistorySubscription(batteryIndex, metric, range);
        }
    }

    private void EnsureBatteryHistorySubscription(int batteryIndex, TelemetryMetric metric, TimeSpan range)
    {
        var key = (batteryIndex, metric);
        if (!_batteryHistorySubscriptions.ContainsKey(key))
        {
            _batteryHistorySubscriptions.Add(key, SubscribeBatteryHistory(batteryIndex, metric, range));
        }
    }

    private void RemoveFanCard(int fanIndex)
    {
        RemoveFanHistory(fanIndex);
        if (_fanCardsByIndex.Remove(fanIndex, out var fan))
        {
            _fans.Remove(fan);
        }
    }

    private void RemoveTemperatureSensor(int sensorIndex)
    {
        RemoveTemperatureHistory(sensorIndex);

        _thermalHistorySeriesBySensorIndex.Remove(sensorIndex);

        if (_thermalSensorsByIndex.Remove(sensorIndex, out var sensor))
        {
            DetachThermalSensorHandler(sensor);
            _thermalSensors.Remove(sensor);
        }

        RefreshThermalHistoryChart();
    }

    private void RemoveBatteryCard(int batteryIndex)
    {
        RemoveBatteryHistorySubscriptions(batteryIndex);
        if (_batteryCardsByIndex.Remove(batteryIndex, out var battery))
        {
            _batteries.Remove(battery);
        }
    }

    private void RemoveBatteryHistorySubscriptions(int batteryIndex)
    {
        foreach (var metric in BatteryHistoryMetrics)
        {
            RemoveBatteryHistory(batteryIndex, metric);
        }
    }

    private ImmutableArray<TemperatureTelemetrySnapshot> GetDrivingSensors(FanControlStateSnapshot? controlState)
    {
        if (controlState is null || controlState.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return [];
        }

        return [.. controlState.DrivingSensorIndices
            .Select(sensorIndex => _temperatureSnapshots.TryGetValue(sensorIndex, out var snapshot) ? snapshot : null)
            .OfType<TemperatureTelemetrySnapshot>()];
    }

    public void SelectHistoryRangeChangeFan(int fanIndex, TimeSpan value)
    {
        RemoveFanHistory(fanIndex);
        _fanHistorySubscriptions[fanIndex] = SubscribeFanHistory(fanIndex, value);
    }

    public void SelectHistoryRangeChangeThermal(int sensorIndex, TimeSpan value)
    {
        RemoveTemperatureHistory(sensorIndex);
        _temperatureHistorySubscriptions[sensorIndex] = SubscribeTemperatureHistory(sensorIndex, value);
    }

    public void SelectHistoryRangeChangePower((int index, TelemetryMetric metric) batteryIndex, TimeSpan value)
    {
        RemoveBatteryHistory(batteryIndex.index, batteryIndex.metric);
        _batteryHistorySubscriptions[batteryIndex] = SubscribeBatteryHistory(batteryIndex.index, batteryIndex.metric, value);
    }

    private IDisposable SubscribeFanHistory(int index, TimeSpan range) =>
        _fanTelemetryClient
            .WatchFanHistory(index, range)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                _fanHistoryPoints[index] =
                [
                    .. pts
                        .OrderBy(x => x.ObservedAt)
                        .ThenBy(x => x.SampleId)
                ];

                RefreshFanHistory(index);
            });

    private void RemoveFanHistory(int index)
    {
        if (_fanHistorySubscriptions.Remove(index, out IDisposable? sub))
        {
            sub.Dispose();
        }

        _fanHistoryPoints.Remove(index);

        if (_fanCardsByIndex.TryGetValue(index, out var fan))
        {
            fan.FanSpeedHistory = [];
        }
    }

    private void RefreshFanHistory(int fanIndex)
    {
        if (_fanCardsByIndex.TryGetValue(fanIndex, out var fan))
        {
            UpdateFanHistory(fan, _fanHistoryPoints.TryGetValue(fanIndex, out var historyPoints) ? historyPoints : []);
        }
    }

    private void UpdateFanHistory(FanCardModel fan, IReadOnlyList<FanTelemetrySeriesPoint> points)
    {
        fan.FanSpeedHistory = points.Count == 0
            ? []
            :
            [
                .. points.Select(point => new DateTimePoint(
                    point.ObservedAt.LocalDateTime,
                    _unitFormattingService.ConvertFanSpeed(point.SpeedRpm)))
            ];
    }

    private void RefreshBatterySensorHistories(PowerCardModel sensor)
    {
        foreach (var metric in BatteryHistoryMetrics)
        {
            RefreshBatterySensorHistory(sensor, metric);
        }
    }

    private void RefreshBatterySensorHistory(PowerCardModel sensor, TelemetryMetric metric)
    {
        var key = (sensor.BatterySnapshot.BatteryIndex, metric);
        UpdateBatterySensorHistory(sensor, metric, _batteryHistoryPoints.TryGetValue(key, out var historyPoints) ? historyPoints : []);
    }

    private void UpdateBatterySensorHistory(PowerCardModel sensor, TelemetryMetric metric, IReadOnlyList<TelemetryPoint> points)
    {
        DateTimePoint[] overviewHistory = points.Count == 0
            ? []
            :
            [
                .. points.Select(point => new DateTimePoint(
                    point.ObservedAt.LocalDateTime,
                    point.NumericValue == 0d ? null : ConvertBatteryMetricValue(metric, point.NumericValue)))
            ];

        if (ShouldAppendCurrentGap(sensor.BatterySnapshot, metric))
        {
            overviewHistory = AppendNullGapPoint(overviewHistory, sensor.BatterySnapshot.ObservedAt.LocalDateTime);
        }

        if (overviewHistory.Length == 0)
        {
            sensor.ClearMetricHistory(metric);
            return;
        }

        var cardStart = overviewHistory[^1].DateTime - TelemetryHistoryLimits.MaximumHistoryWindow;
        var cardHistory = overviewHistory
            .Where(point => point.DateTime >= cardStart)
            .ToArray();

        sensor.UpdateMetricHistory(metric, overviewHistory, cardHistory);
    }

    private void RefreshThermalSensorHistory(ThermalSensorModel sensor)
    {
        UpdateThermalSensorHistory(sensor, _temperatureHistoryPoints.TryGetValue(sensor.Snapshot.SensorIndex, out var historyPoints) ? historyPoints : []);
    }

    private void AttachThermalSensorHandler(ThermalSensorModel sensor)
    {
        if (_thermalSensorHandlers.ContainsKey(sensor))
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
        _thermalSensorHandlers[sensor] = handler;
    }

    private void DetachThermalSensorHandler(ThermalSensorModel sensor)
    {
        if (_thermalSensorHandlers.Remove(sensor, out var handler))
        {
            sensor.PropertyChanged -= handler;
        }
    }

    private void DetachThermalSensorHandlers()
    {
        foreach (var sensor in _thermalSensorHandlers.Keys.ToArray())
        {
            DetachThermalSensorHandler(sensor);
        }
    }

    private void RefreshThermalHistoryChart()
    {
        var selectedSensors = GetSelectedThermalSensors();

        UpdateThermalHistoryAxis(selectedSensors);
        ThermalHistorySeries = [.. selectedSensors.Select(GetOrCreateThermalHistorySeries)];
    }

    private void UpdateThermalHistoryAxis()
    {
        UpdateThermalHistoryAxis(GetSelectedThermalSensors());
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
            TelemetryHistoryLimits.MaximumHistoryWindow,
            PresentationDefaults.StandardTelemetryHistorySeparatorStep);

        ThermalHistoryMinLimit = axisStart.Ticks;
        ThermalHistoryMaxLimit = axisEnd.Ticks;
        ThermalHistorySeparators = separators;
    }

    private ThermalSensorModel[] GetSelectedThermalSensors()
    {
        return [.. VisibleThermalSensors.Where(sensor => sensor.IsSelected)];
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

    private void UpdateThermalSensorHistory(ThermalSensorModel sensor, IReadOnlyList<TelemetryPoint> points)
    {
        DateTimePoint[] overviewHistory = points.Count == 0
            ? []
            :
            [
                .. points.Select(point => new DateTimePoint(
                    point.ObservedAt.LocalDateTime,
                    point.NumericValue == 0d ? null : _unitFormattingService.ConvertTemperature(point.NumericValue)))
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

        var cardStart = overviewHistory[^1].DateTime - PresentationDefaults.RecentTelemetryHistoryWindow;
        var cardHistory = overviewHistory
            .Where(point => point.DateTime >= cardStart)
            .ToArray();

        sensor.UpdateTemperatureHistory(overviewHistory, cardHistory);
    }

    private double ConvertBatteryMetricValue(TelemetryMetric metric, double value)
    {
        return metric switch
        {
            TelemetryMetric.BatteryChargePercent => _unitFormattingService.ConvertRatio(value),
            TelemetryMetric.BatteryPresentRateAmperes => _unitFormattingService.ConvertCurrent(value),
            TelemetryMetric.BatteryPresentVoltageVolts => _unitFormattingService.ConvertVoltage(value),
            _ => value,
        };
    }

    private static bool ShouldAppendCurrentGap(TemperatureTelemetrySnapshot snapshot)
    {
        if (!snapshot.IsAvailable || snapshot.TemperatureCelsius is null)
        {
            return true;
        }

        return snapshot.TemperatureState is not null && snapshot.TemperatureState != FrameworkTemperatureState.Ok;
    }

    private static bool ShouldAppendCurrentGap(BatteryTelemetrySnapshot snapshot, TelemetryMetric metric)
    {
        if (!snapshot.IsAvailable)
        {
            return true;
        }

        if (snapshot.BatteryState == FrameworkBatteryState.NotPresent)
        {
            return true;
        }

        var metricValue = metric switch
        {
            TelemetryMetric.BatteryChargePercent => snapshot.ChargePercent,
            TelemetryMetric.BatteryPresentRateAmperes => snapshot.Amperage,
            TelemetryMetric.BatteryPresentVoltageVolts => snapshot.Voltage,
            _ => null,
        };

        return metricValue is null;
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

    private static bool ShouldShowThermalSensorByDefault(TemperatureTelemetrySnapshot snapshot)
    {
        return snapshot.IsAvailable;
    }

    private IDisposable SubscribeTemperatureHistory(int index, TimeSpan value) =>
        _temperatureTelemetryClient
            .WatchTemperatureHistory(index, value)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                _temperatureHistoryPoints[index] =
                [
                    .. pts
                        .OrderBy(x => x.ObservedAt)
                        .ThenBy(x => x.SampleId)
                ];

                if (_thermalSensorsByIndex.TryGetValue(index, out var sensor))
                {
                    RefreshThermalSensorHistory(sensor);
                }
            });

    private void RemoveTemperatureHistory(int index)
    {
        if (_temperatureHistorySubscriptions.Remove(index, out IDisposable? sub))
        {
            sub.Dispose();
        }

        _temperatureHistoryPoints.Remove(index);

        if (_thermalSensorsByIndex.TryGetValue(index, out var sensor))
        {
            sensor.ClearTemperatureHistory();
        }
    }

    private IDisposable SubscribeBatteryHistory(int index, TelemetryMetric metric, TimeSpan value) =>
        _batteryTelemetryClient
            .WatchBatteryHistory(index, metric, value)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                _batteryHistoryPoints[(index, metric)] =
                [
                    .. pts
                        .OrderBy(x => x.ObservedAt)
                        .ThenBy(x => x.SampleId)
                ];

                if (_batteryCardsByIndex.TryGetValue(index, out var battery))
                {
                    RefreshBatterySensorHistory(battery, metric);
                }
            });

    private void RemoveBatteryHistory(int index, TelemetryMetric metric)
    {
        var key = (index, metric);
        if (_batteryHistorySubscriptions.Remove(key, out IDisposable? sub))
        {
            sub.Dispose();
        }

        _batteryHistoryPoints.Remove(key);

        if (_batteryCardsByIndex.TryGetValue(index, out var battery))
        {
            battery.ClearMetricHistory(metric);
        }
    }

    private Task RefreshUnitFormattingAsync()
    {
        foreach (var fan in _fans)
        {
            fan.RefreshUnitFormatting();
            RefreshFanHistory(fan.Snapshot.FanIndex);
        }

        foreach (var sensor in _thermalSensors)
        {
            sensor.RefreshUnitFormatting();
            RefreshThermalSensorHistory(sensor);
        }

        RefreshThermalHistoryChart();
        UnitFormattingRevision++;

        foreach (var battery in _batteries)
        {
            battery.RefreshUnitFormatting();
            RefreshBatterySensorHistories(battery);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        DetachThermalSensorHandlers();
        _subscriptions.Dispose();
        foreach (var sub in _fanHistorySubscriptions.Values) sub.Dispose();
        foreach (var sub in _temperatureHistorySubscriptions.Values) sub.Dispose();
        foreach (var sub in _batteryHistorySubscriptions.Values) sub.Dispose();
    }
}

