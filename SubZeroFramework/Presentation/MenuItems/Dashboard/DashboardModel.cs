using System.Reactive.Disposables;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

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
    private readonly SynchronizationContext _synchronizationContext;
    private readonly TimeSpan _initialTimeSpan = TimeSpan.FromSeconds(30);

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

        _subscriptions.Add(_frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(status => LastStatus = status));

        _subscriptions.Add(_fanCapabilityClient
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
            }));

        _subscriptions.Add(_fanControlStateClient
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
            }));

        _subscriptions.Add(_fanStateClient
            .WatchFanStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    var fan = Fans.FirstOrDefault(existingFan => existingFan.Snapshot.FanIndex == change.Key);

                    if (change.Reason == ChangeReason.Remove)
                    {
                        _fanStates.Remove(change.Key);
                        if (fan != null)
                        {
                            fan.FanState = null;
                        }

                        continue;
                    }

                    _fanStates[change.Key] = change.Current;
                    if (fan != null)
                    {
                        fan.FanState = change.Current;
                    }
                }
            }));

        // Sync Current Fans
        _subscriptions.Add(_fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                var newFans = Fans.ToBuilder();
                bool changed = false;
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
                    }
                    else if (change.Reason == ChangeReason.Update || change.Reason == ChangeReason.Refresh)
                    {
                        var fan = newFans.FirstOrDefault(f => f.Snapshot.FanIndex == change.Current.FanIndex);
                        if (fan != null)
                        {
                            fan.Snapshot = change.Current;
                        }
                    }
                    else if (change.Reason == ChangeReason.Remove)
                    {
                        var fan = newFans.FirstOrDefault(f => f.Snapshot.FanIndex == change.Current.FanIndex);
                        if (fan != null)
                        {
                            newFans.Remove(fan);
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    Fans = newFans.ToImmutable();
                }
            }));

        // Sync Current Temperatures
        _subscriptions.Add(_temperatureTelemetryClient
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
            }));

        // Sync Current Batteries
        _subscriptions.Add(_batteryTelemetryClient
            .WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .ToCollection()
            .Subscribe(collection => BatteryTelemetries = [.. collection]));

        // Manage Fan History Series
        _subscriptions.Add(_fanTelemetryClient
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
            }));

        // Manage Temperature History Series
        _subscriptions.Add(_temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    if (change.Reason == ChangeReason.Add && !_temperatureHistorySubscriptions.ContainsKey(change.Key))
                        _temperatureHistorySubscriptions.Add(change.Key, SubscribeTemperatureHistory(change.Key, _initialTimeSpan));
                    else if (change.Reason == ChangeReason.Remove)
                        RemoveTemperatureHistory(change.Key);
                }
            }));

        // Manage Battery History Series (watching Charge Percent only as an example)
        _subscriptions.Add(_batteryTelemetryClient
            .WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .Subscribe(set =>
            {
                foreach (var change in set)
                {
                    var batteryKey = (change.Key, TelemetryMetric.BatteryChargePercent);
                    if (change.Reason == ChangeReason.Add && !_batteryHistorySubscriptions.ContainsKey(batteryKey))
                        _batteryHistorySubscriptions.Add(batteryKey, SubscribeBatteryHistory(batteryKey.Item1, batteryKey.Item2, _initialTimeSpan));
                    else if (change.Reason == ChangeReason.Remove)
                        RemoveBatteryHistory(batteryKey.Item1, batteryKey.Item2);
                }
            }));
    }

    [ObservableProperty]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    [ObservableProperty]
    public partial ImmutableArray<FanCardModel> Fans { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<TemperatureTelemetrySnapshot> TemperatureTelemetries { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<BatteryTelemetrySnapshot> BatteryTelemetries { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<TemperatureTelemetryHistorySeries> TemperatureHistory { get; set; } = [];

    [ObservableProperty]
    public partial ImmutableArray<BatteryTelemetryHistorySeries> BatteryHistory { get; set; } = [];

    private void UpdateDrivingSensors(FanCardModel fan)
    {
        fan.DrivingSensors = GetDrivingSensors(fan.ControlState);
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
            });

    private void RemoveTemperatureHistory(int index)
    {
        if (_temperatureHistorySubscriptions.Remove(index, out IDisposable? sub)) sub.Dispose();
        var existing = TemperatureHistory.FirstOrDefault(s => s.SensorIndex == index);
        if (existing != null) TemperatureHistory = TemperatureHistory.Remove(existing);
    }

    private IDisposable SubscribeBatteryHistory(int index, TelemetryMetric metric,TimeSpan value) =>
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
            });

    private void RemoveBatteryHistory(int index, TelemetryMetric metric)
    {
        var key = (index, metric);
        if (_batteryHistorySubscriptions.Remove(key, out IDisposable? sub)) sub.Dispose();
        var existing = BatteryHistory.FirstOrDefault(s => s.BatteryIndex == index && s.Metric == metric);
        if (existing != null) BatteryHistory = BatteryHistory.Remove(existing);
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        foreach (var sub in _fanHistorySubscriptions.Values) sub.Dispose();
        foreach (var sub in _temperatureHistorySubscriptions.Values) sub.Dispose();
        foreach (var sub in _batteryHistorySubscriptions.Values) sub.Dispose();
    }
}

