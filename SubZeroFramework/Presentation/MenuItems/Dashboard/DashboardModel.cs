using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class DashboardModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Dictionary<int, FanCapabilityState> _fanCapabilities = [];
    private readonly Dictionary<int, FanControlStateSnapshot> _fanControlStates = [];
    private readonly Dictionary<int, FanStateSnapshot> _fanStates = [];
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly IFrameworkTelemetryClient _frameworkTelemetryClient;
    private readonly IFanCapabilityClient _fanCapabilityClient;
    private readonly IFanControlStateClient _fanControlStateClient;
    private readonly IFanStateClient _fanStateClient;
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly IBatteryTelemetryClient _batteryTelemetryClient;
    private readonly ObservableCollection<ThermalSensorModel> _visibleThermalSensors = [];
    private readonly SynchronizationContext _synchronizationContext;
    private readonly TimeSpan _initialTimeSpan = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _temperatureHistoryWindow = TimeSpan.FromHours(1);
    private readonly TimeSpan _thermalCardHistoryWindow = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _batteryCardHistoryWindow = TimeSpan.FromHours(1);
    // Trackers for inner subscriptions
    private readonly Dictionary<int, IDisposable> _fanHistorySubscriptions = [];
    private readonly Dictionary<int, IDisposable> _temperatureHistorySubscriptions = [];
    private readonly Dictionary<(int Index, TelemetryMetric Metric), IDisposable> _batteryHistorySubscriptions = [];

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
        SynchronizationContext synchronizationContext)
    {
        _frameworkStatusClient = frameworkStatusClient;
        _frameworkTelemetryClient = frameworkTelemetryClient;
        _fanCapabilityClient = fanCapabilityClient;
        _fanControlStateClient = fanControlStateClient;
        _fanStateClient = fanStateClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _batteryTelemetryClient = batteryTelemetryClient;
        _synchronizationContext = synchronizationContext;
        VisibleThermalSensors = new ReadOnlyObservableCollection<ThermalSensorModel>(_visibleThermalSensors);

        _frameworkStatusClient
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
                    var fan = Fans.FirstOrDefault(existingFan => existingFan.Snapshot.FanIndex == change.Key);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanCapabilities.Remove(change.Key);
                        if (fan != null)
                        {
                            fan.Capability = null;
                        }

                        continue;
                    }

                    _fanCapabilities[change.Key] = change.Current;
                    if (fan != null)
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
                    var fan = Fans.FirstOrDefault(existingFan => existingFan.Snapshot.FanIndex == change.Key);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanControlStates.Remove(change.Key);
                        if (fan != null)
                        {
                            fan.ControlState = null;
                            fan.DrivingSensors = [];
                        }

                        continue;
                    }

                    _fanControlStates[change.Key] = change.Current;
                    if (fan != null)
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
                var visibleFansChanged = false;

                foreach (var change in set)
                {
                    var fan = Fans.FirstOrDefault(existingFan => existingFan.Snapshot.FanIndex == change.Key);
                    var wasVisible = fan is not null && IsFanVisible(fan);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanStates.Remove(change.Key);
                        if (fan != null)
                        {
                            fan.FanState = null;
                            visibleFansChanged |= wasVisible != IsFanVisible(fan);
                        }

                        continue;
                    }

                    _fanStates[change.Key] = change.Current;
                    if (fan != null)
                    {
                        fan.FanState = change.Current;
                        visibleFansChanged |= wasVisible != IsFanVisible(fan);
                    }
                }

                if (visibleFansChanged)
                {
                    VisibleFansRevision++;
                }
            })
            .DisposeWith(_subscriptions);

        // Sync Current Fans
        _fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                var newFans = Fans.ToBuilder();
                bool changed = false;
                var visibleFansChanged = false;
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        var fan = new FanCardModel
                        {
                            Snapshot = change.Current,
                            Capability = _fanCapabilities.GetValueOrDefault(change.Current.FanIndex),
                            ControlState = _fanControlStates.GetValueOrDefault(change.Current.FanIndex),
                            DrivingSensors = GetDrivingSensors(_fanControlStates.GetValueOrDefault(change.Current.FanIndex)),
                            FanState = _fanStates.GetValueOrDefault(change.Current.FanIndex),
                        };
                        newFans.Add(fan);
                        changed = true;
                        visibleFansChanged |= IsFanVisible(fan);
                    }
                    else if (change.Reason == ChangeReason.Update || change.Reason == ChangeReason.Refresh)
                    {
                        var fan = newFans.FirstOrDefault(f => f.Snapshot.FanIndex == change.Current.FanIndex);
                        if (fan != null)
                        {
                            var wasVisible = IsFanVisible(fan);
                            fan.Snapshot = change.Current;
                            visibleFansChanged |= wasVisible != IsFanVisible(fan);
                        }
                    }
                    else if (change.Reason == ChangeReason.Remove)
                    {
                        var fan = newFans.FirstOrDefault(f => f.Snapshot.FanIndex == change.Current.FanIndex);
                        if (fan != null)
                        {
                            visibleFansChanged |= IsFanVisible(fan);
                            newFans.Remove(fan);
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    Fans = newFans.ToImmutable();
                }

                if (visibleFansChanged)
                {
                    VisibleFansRevision++;
                }
            })
            .DisposeWith(_subscriptions);

        // Sync Current Temperatures
        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                var newSensors = ThermalSensors.ToBuilder();
                var changed = false;
                var visibleSensorsChanged = false;

                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        var thermalSensor = new ThermalSensorModel
                        {
                            Snapshot = change.Current,
                            IsSelected = ShouldShowThermalSensorByDefault(change.Current)
                                && newSensors.Count(existingSensor => existingSensor.IsSelected) < 4,
                        };

                        RefreshThermalSensorHistory(thermalSensor);

                        newSensors.Add(thermalSensor);
                        changed = true;
                        visibleSensorsChanged |= IsThermalSensorVisible(thermalSensor);
                        continue;
                    }

                    var existingSensor = newSensors.FirstOrDefault(sensor => sensor.Snapshot.SensorIndex == change.Key);
                    if (existingSensor is null)
                    {
                        continue;
                    }

                    if (change.Reason == ChangeReason.Remove)
                    {
                        visibleSensorsChanged |= IsThermalSensorVisible(existingSensor);
                        newSensors.Remove(existingSensor);
                        changed = true;
                        continue;
                    }

                    var wasVisible = IsThermalSensorVisible(existingSensor);
                    existingSensor.Snapshot = change.Current;
                    RefreshThermalSensorHistory(existingSensor);
                    visibleSensorsChanged |= wasVisible != IsThermalSensorVisible(existingSensor);
                }

                if (changed)
                {
                    ThermalSensors = [.. newSensors.OrderBy(sensor => sensor.Snapshot.SensorIndex)];
                }

                if (changed || visibleSensorsChanged)
                {
                    SynchronizeVisibleThermalSensors();
                }
            })
            .DisposeWith(_subscriptions);

        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .ToCollection()
            .Subscribe(collection =>
            {
                TemperatureTelemetries = [.. collection];

                foreach (var fan in Fans)
                {
                    UpdateDrivingSensors(fan);
                }
            })
            .DisposeWith(_subscriptions);

        // Sync Current Batteries
        _batteryTelemetryClient
            .WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                var newBatteries = Batteries.ToBuilder();
                var changed = false;
                var snapshotsChanged = false;

                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        var battery = new PowerCardModel
                        {
                            BatterySnapshot = change.Current
                        };

                        RefreshBatterySensorHistory(battery, TelemetryMetric.BatteryChargePercent);
                        RefreshBatterySensorHistory(battery, TelemetryMetric.BatteryPresentRateAmperes);
                        RefreshBatterySensorHistory(battery, TelemetryMetric.BatteryPresentVoltageVolts);

                        newBatteries.Add(battery);
                        changed = true;
                        snapshotsChanged = true;
                        continue;
                    }

                    var existingBattery = newBatteries.FirstOrDefault(item => item.BatterySnapshot.BatteryIndex == change.Key);
                    if (existingBattery is null)
                    {
                        continue;
                    }

                    if (change.Reason == ChangeReason.Remove)
                    {
                        newBatteries.Remove(existingBattery);
                        changed = true;
                        snapshotsChanged = true;
                        continue;
                    }

                    existingBattery.BatterySnapshot = change.Current;
                    RefreshBatterySensorHistory(existingBattery, TelemetryMetric.BatteryChargePercent);
                    RefreshBatterySensorHistory(existingBattery, TelemetryMetric.BatteryPresentRateAmperes);
                    RefreshBatterySensorHistory(existingBattery, TelemetryMetric.BatteryPresentVoltageVolts);
                    snapshotsChanged = true;
                }

                if (changed)
                {
                    Batteries = [.. newBatteries.OrderBy(battery => battery.BatterySnapshot.BatteryIndex)];
                }

                if (changed || snapshotsChanged)
                {
                    BatteryTelemetries = [.. Batteries.Select(battery => battery.BatterySnapshot)];
                }
            })
            .DisposeWith(_subscriptions);

        // Manage Fan History Series
        _fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add && !_fanHistorySubscriptions.ContainsKey(change.Key))
                        _fanHistorySubscriptions.Add(change.Key, SubscribeFanHistory(change.Key, _initialTimeSpan));
                    else if (change.Reason == ChangeReason.Remove)
                        RemoveFanHistory(change.Key);
                }
            })
            .DisposeWith(_subscriptions);

        // Manage Temperature History Series
        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add && !_temperatureHistorySubscriptions.ContainsKey(change.Key))
                        _temperatureHistorySubscriptions.Add(change.Key, SubscribeTemperatureHistory(change.Key, _temperatureHistoryWindow));
                    else if (change.Reason == ChangeReason.Remove)
                        RemoveTemperatureHistory(change.Key);
                }
            })
            .DisposeWith(_subscriptions);

        // Manage Battery History Series
        _batteryTelemetryClient
            .WatchBatteryHistory(0, TelemetryMetric.BatteryChargePercent, _batteryCardHistoryWindow)
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        EnsureBatteryHistorySubscriptions(change.Current.ChannelId.Index, TelemetryMetric.BatteryChargePercent, _batteryCardHistoryWindow);
                    }
                    else if (change.Reason == ChangeReason.Remove)
                    {
                        RemoveBatteryHistorySubscriptions(change.Current.ChannelId.Index, TelemetryMetric.BatteryChargePercent);
                    }
                    else
                    {
                        ;
                    }
                }
            })
            .DisposeWith(_subscriptions);
        _batteryTelemetryClient
            .WatchBatteryHistory(0, TelemetryMetric.BatteryPresentRateAmperes, TimeSpan.FromHours(1))
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        EnsureBatteryHistorySubscriptions(change.Current.ChannelId.Index, TelemetryMetric.BatteryPresentRateAmperes, _batteryCardHistoryWindow);
                    }
                    else if (change.Reason == ChangeReason.Remove)
                    {
                        RemoveBatteryHistorySubscriptions(change.Current.ChannelId.Index, TelemetryMetric.BatteryPresentRateAmperes);
                    }
                }
            })
            .DisposeWith(_subscriptions);
        _batteryTelemetryClient
            .WatchBatteryHistory(0, TelemetryMetric.BatteryPresentVoltageVolts, TimeSpan.FromHours(1))
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add)
                    {
                        EnsureBatteryHistorySubscriptions(change.Current.ChannelId.Index, TelemetryMetric.BatteryPresentVoltageVolts, _batteryCardHistoryWindow);
                    }
                    else if (change.Reason == ChangeReason.Remove)
                    {
                        RemoveBatteryHistorySubscriptions(change.Current.ChannelId.Index, TelemetryMetric.BatteryPresentVoltageVolts);
                    }
                }
            })
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    [ObservableProperty]
    public partial ImmutableArray<FanCardModel> Fans { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<TemperatureTelemetrySnapshot> TemperatureTelemetries { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<ThermalSensorModel> ThermalSensors { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<BatteryTelemetrySnapshot> BatteryTelemetries { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<TemperatureTelemetryHistorySeries> TemperatureHistory { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<BatteryTelemetryHistorySeries> BatteryHistory { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<PowerCardModel> Batteries { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleFans))]
    public partial int VisibleFansRevision { get; set; }

    public ImmutableArray<FanCardModel> VisibleFans => [.. Fans.Where(IsFanVisible)];

    public ReadOnlyObservableCollection<ThermalSensorModel> VisibleThermalSensors { get; }

    private void UpdateDrivingSensors(FanCardModel fan)
    {
        fan.DrivingSensors = GetDrivingSensors(fan.ControlState);
    }

    private void SynchronizeVisibleThermalSensors()
    {
        var visibleSensors = ThermalSensors
            .Where(IsThermalSensorVisible)
            .ToArray();

        SynchronizeSensors(_visibleThermalSensors, visibleSensors);
    }

    private static void SynchronizeSensors(ObservableCollection<ThermalSensorModel> target, IReadOnlyList<ThermalSensorModel> source)
    {
        for (var index = 0; index < source.Count; index++)
        {
            var expectedSensor = source[index];

            if (index < target.Count && ReferenceEquals(target[index], expectedSensor))
            {
                continue;
            }

            var existingIndex = target.IndexOf(expectedSensor);
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
                continue;
            }

            target.Insert(index, expectedSensor);
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private ImmutableArray<TemperatureTelemetrySnapshot> GetDrivingSensors(FanControlStateSnapshot? controlState)
    {
        if (controlState is null || controlState.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            return [];
        }

        var temperaturesByIndex = TemperatureTelemetries.ToDictionary(snapshot => snapshot.SensorIndex);

        return [.. controlState.DrivingSensorIndices
            .Select(sensorIndex => temperaturesByIndex.TryGetValue(sensorIndex, out var snapshot) ? snapshot : null)
            .OfType<TemperatureTelemetrySnapshot>()];
    }

    public void SelectHistoryRangeChangeFan(int fanIndex, TimeSpan value)
    {
        if (_fanHistorySubscriptions.TryGetValue(fanIndex, out IDisposable? disp))
        {
            disp?.Dispose();
            _fanHistorySubscriptions.Remove(fanIndex);
        }
        _fanHistorySubscriptions.Add(fanIndex, SubscribeFanHistory(fanIndex, value));
    }
    public void SelectHistoryRangeChangeThermal(int sensorIndex, TimeSpan value)
    {
        if (_temperatureHistorySubscriptions.TryGetValue(sensorIndex, out IDisposable? disp))
        {
            disp?.Dispose();
            _temperatureHistorySubscriptions.Remove(sensorIndex);
        }
        _temperatureHistorySubscriptions.Add(sensorIndex, SubscribeTemperatureHistory(sensorIndex, value));
    }
    public void SelectHistoryRangeChangePower((int index, TelemetryMetric metric) batteryIndex, TimeSpan value)
    {
        if (_batteryHistorySubscriptions.TryGetValue(batteryIndex, out IDisposable? disp))
        {
            disp?.Dispose();
            _batteryHistorySubscriptions.Remove(batteryIndex);
        }
        _batteryHistorySubscriptions.Add(batteryIndex, SubscribeBatteryHistory(batteryIndex.index, batteryIndex.metric, value));
    }

    private void EnsureBatteryHistorySubscriptions(int batteryIndex, TelemetryMetric metric, TimeSpan range)
    {
        var key = (batteryIndex, metric);
        if (!_batteryHistorySubscriptions.ContainsKey(key))
        {
            _batteryHistorySubscriptions.Add(key, SubscribeBatteryHistory(batteryIndex, metric, range));
        }
    }

    private void RemoveBatteryHistorySubscriptions(int batteryIndex, TelemetryMetric metric)
    {
        RemoveBatteryHistory(batteryIndex, metric);
    }

    private IDisposable SubscribeFanHistory(int index, TimeSpan range) =>
        _fanTelemetryClient
            .WatchFanHistory(index, range)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                var existingFan = Fans.FirstOrDefault(f => f.Snapshot.FanIndex == index);
                if (existingFan != null)
                {
                    existingFan.FanSpeedHistory = pts
                        .OrderBy(x => x.ObservedAt)
                        .ThenBy(x => x.SampleId)
                        .Select(x => new DateTimePoint(x.ObservedAt.LocalDateTime, x.SpeedRpm))
                        .ToArray();
                }
            });

    private void RemoveFanHistory(int index)
    {
        if (_fanHistorySubscriptions.Remove(index, out IDisposable? sub)) sub.Dispose();
        var existingFan = Fans.FirstOrDefault(f => f.Snapshot.FanIndex == index);
        if (existingFan != null)
        {
            existingFan.FanSpeedHistory = [];
        }
    }

    private IDisposable SubscribeTemperatureHistory(int index, TimeSpan value) =>
        _temperatureTelemetryClient
            .WatchTemperatureHistory(index, value)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                var existing = TemperatureHistory.FirstOrDefault(s => s.SensorIndex == index);
                var newData = new TemperatureTelemetryHistorySeries
                {
                    SensorIndex = index,
                    Points =
                    [
                        .. pts
                            .OrderBy(x => x.ObservedAt)
                            .ThenBy(x => x.SampleId)
                    ]
                };
                TemperatureHistory = existing != null ? TemperatureHistory.Replace(existing, newData) : TemperatureHistory.Add(newData);

                var thermalSensor = ThermalSensors.FirstOrDefault(sensor => sensor.Snapshot.SensorIndex == index);
                if (thermalSensor is not null)
                {
                    RefreshThermalSensorHistory(thermalSensor);
                }
            });

    private void RemoveTemperatureHistory(int index)
    {
        if (_temperatureHistorySubscriptions.Remove(index, out IDisposable? sub)) sub.Dispose();
        var existing = TemperatureHistory.FirstOrDefault(s => s.SensorIndex == index);
        if (existing != null) TemperatureHistory = TemperatureHistory.Remove(existing);

        var thermalSensor = ThermalSensors.FirstOrDefault(sensor => sensor.Snapshot.SensorIndex == index);
        if (thermalSensor is not null)
        {
            thermalSensor.ClearTemperatureHistory();
        }
    }

    private void RefreshBatterySensorHistory(PowerCardModel sensor, TelemetryMetric metric)
    {
        var existingHistory = BatteryHistory.FirstOrDefault(series => series.BatteryIndex == sensor.BatterySnapshot.BatteryIndex && series.Metric == metric);
        UpdateBatterySensorHistory(sensor, metric, existingHistory is null ? [] : existingHistory.Points);
    }

    private void UpdateBatterySensorHistory(PowerCardModel sensor, TelemetryMetric metric, ImmutableArray<TelemetryPoint> points)
    {
        DateTimePoint[] overviewHistory = points.IsDefaultOrEmpty
            ? []
            :
            [
                .. points.Select(point => new DateTimePoint(
                    point.ObservedAt.LocalDateTime,
                    point.NumericValue == 0d ? null : point.NumericValue))
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

        var cardStart = overviewHistory[^1].DateTime - _batteryCardHistoryWindow;
        var cardHistory = overviewHistory
            .Where(point => point.DateTime >= cardStart)
            .ToArray();

        sensor.UpdateMetricHistory(metric, overviewHistory, cardHistory);
    }

    private void RefreshThermalSensorHistory(ThermalSensorModel sensor)
    {
        var existingHistory = TemperatureHistory.FirstOrDefault(series => series.SensorIndex == sensor.Snapshot.SensorIndex);
        UpdateThermalSensorHistory(sensor, existingHistory is null ? [] : existingHistory.Points);
    }

    private void UpdateThermalSensorHistory(ThermalSensorModel sensor, ImmutableArray<TelemetryPoint> points)
    {
        DateTimePoint[] overviewHistory = points.IsDefaultOrEmpty
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

        var cardStart = overviewHistory[^1].DateTime - _thermalCardHistoryWindow;
        var cardHistory = overviewHistory
            .Where(point => point.DateTime >= cardStart)
            .ToArray();

        sensor.UpdateTemperatureHistory(overviewHistory, cardHistory);
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

    private static bool IsThermalSensorVisible(ThermalSensorModel sensor)
    {
        return true;
    }

    private static bool IsFanVisible(FanCardModel fan)
    {
        return true;
    }

    private IDisposable SubscribeBatteryHistory(int index, TelemetryMetric metric, TimeSpan value) =>
        _batteryTelemetryClient
            .WatchBatteryHistory(index, metric, value)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                var existing = BatteryHistory.FirstOrDefault(s => s.BatteryIndex == index && s.Metric == metric);
                var newData = new BatteryTelemetryHistorySeries
                {
                    BatteryIndex = index,
                    Metric = metric,
                    Points =
                    [
                        .. pts
                            .OrderBy(x => x.ObservedAt)
                            .ThenBy(x => x.SampleId)
                    ]
                };
                BatteryHistory = existing != null ? BatteryHistory.Replace(existing, newData) : BatteryHistory.Add(newData);

                var existingBattery = Batteries.FirstOrDefault(battery => battery.BatterySnapshot.BatteryIndex == index);
                if (existingBattery is not null)
                {
                    RefreshBatterySensorHistory(existingBattery, metric);
                }
            });

    private void RemoveBatteryHistory(int index, TelemetryMetric metric)
    {
        var key = (index, metric);
        if (_batteryHistorySubscriptions.Remove(key, out IDisposable? sub)) sub.Dispose();
        var existing = BatteryHistory.FirstOrDefault(s => s.BatteryIndex == index && s.Metric == metric);
        if (existing != null) BatteryHistory = BatteryHistory.Remove(existing);

        var existingBattery = Batteries.FirstOrDefault(battery => battery.BatterySnapshot.BatteryIndex == index);
        if (existingBattery is not null)
        {
            existingBattery.ClearMetricHistory(metric);
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (var sub in _fanHistorySubscriptions.Values) sub.Dispose();
        foreach (var sub in _temperatureHistorySubscriptions.Values) sub.Dispose();
        foreach (var sub in _batteryHistorySubscriptions.Values) sub.Dispose();
    }
}

