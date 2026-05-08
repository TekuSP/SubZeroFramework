using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DynamicData;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class DashboardModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly IFrameworkTelemetryClient _frameworkTelemetryClient;
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly IBatteryTelemetryClient _batteryTelemetryClient;
    private readonly SynchronizationContext _synchronizationContext;

    // Trackers for inner subscriptions
    private readonly Dictionary<int, IDisposable> _fanHistorySubscriptions = [];
    private readonly Dictionary<int, IDisposable> _temperatureHistorySubscriptions = [];
    private readonly Dictionary<(int Index, TelemetryMetric Metric), IDisposable> _batteryHistorySubscriptions = [];

    public DashboardModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        IFrameworkTelemetryClient frameworkTelemetryClient,
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        SynchronizationContext synchronizationContext)
    {
        _frameworkStatusClient = frameworkStatusClient;
        _frameworkTelemetryClient = frameworkTelemetryClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _batteryTelemetryClient = batteryTelemetryClient;
        _synchronizationContext = synchronizationContext;

        _subscriptions.Add(_frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(status => LastStatus = status));

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
                        newFans.Add(new FanCardModel() { Snapshot = change.Current });
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
            .Subscribe(collection => TemperatureTelemetries = [.. collection]));

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
                        _fanHistorySubscriptions.Add(change.Key, SubscribeFanHistory(change.Key));
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
                        _temperatureHistorySubscriptions.Add(change.Key, SubscribeTemperatureHistory(change.Key));
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
                        _batteryHistorySubscriptions.Add(batteryKey, SubscribeBatteryHistory(batteryKey.Item1, batteryKey.Item2));
                    else if (change.Reason == ChangeReason.Remove)
                        RemoveBatteryHistory(batteryKey.Item1, batteryKey.Item2);
                }
            }));
    }

    [ObservableProperty]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    [ObservableProperty]
    public partial TimeSpan SelectedHistoryRange { get; set; } = TimeSpan.FromMinutes(5);

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

    partial void OnSelectedHistoryRangeChanged(TimeSpan value)
    {
        // Fan
        var activeFans = _fanHistorySubscriptions.Keys.ToList();
        foreach (var index in activeFans)
        {
            _fanHistorySubscriptions[index].Dispose();
            _fanHistorySubscriptions[index] = SubscribeFanHistory(index);
        }

        // Temperature
        TemperatureHistory = [];
        var activeTemps = _temperatureHistorySubscriptions.Keys.ToList();
        foreach (var index in activeTemps)
        {
            _temperatureHistorySubscriptions[index].Dispose();
            _temperatureHistorySubscriptions[index] = SubscribeTemperatureHistory(index);
        }

        // Battery
        BatteryHistory = [];
        var activeBatteries = _batteryHistorySubscriptions.Keys.ToList();
        foreach (var key in activeBatteries)
        {
            _batteryHistorySubscriptions[key].Dispose();
            _batteryHistorySubscriptions[key] = SubscribeBatteryHistory(key.Index, key.Metric);
        }
    }

    private IDisposable SubscribeFanHistory(int index) =>
        _fanTelemetryClient
            .WatchFanHistory(index, SelectedHistoryRange)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                var existingFan = Fans.FirstOrDefault(f => f.Snapshot.FanIndex == index);
                if (existingFan != null)
                {
                    if (existingFan.SparklineSeries.Count == 0)
                    {
                        //existingFan.SparklineSeries.Add(new LineSeries<double>
                        //{
                        //    Values = pts.Select(p => p.SpeedRpm).ToArray(),
                        //    LineSmoothness = 1,
                        //    GeometrySize = 0,
                        //    GeometryStroke = null,
                        //    Fill = new SolidColorPaint(SKColors.CornflowerBlue.WithAlpha(50)),
                        //    Stroke = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 2 }
                        //});
                    }
                    else
                    {
                        //((LineSeries<double>)existingFan.SparklineSeries[0]).Values = pts.Select(p => p.SpeedRpm).ToArray();
                    }
                }
            });

    private void RemoveFanHistory(int index)
    {
        if (_fanHistorySubscriptions.Remove(index, out IDisposable? sub)) sub.Dispose();
        var existingFan = Fans.FirstOrDefault(f => f.Snapshot.FanIndex == index);
        if (existingFan != null)
        {
            existingFan.SparklineSeries.Clear();
        }
    }

    private IDisposable SubscribeTemperatureHistory(int index) =>
        _temperatureTelemetryClient
            .WatchTemperatureHistory(index, SelectedHistoryRange)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                var existing = TemperatureHistory.FirstOrDefault(s => s.SensorIndex == index);
                var newData = new TemperatureTelemetryHistorySeries { SensorIndex = index, Points = [.. pts] };
                TemperatureHistory = existing != null ? TemperatureHistory.Replace(existing, newData) : TemperatureHistory.Add(newData);
            });

    private void RemoveTemperatureHistory(int index)
    {
        if (_temperatureHistorySubscriptions.Remove(index, out IDisposable? sub)) sub.Dispose();
        var existing = TemperatureHistory.FirstOrDefault(s => s.SensorIndex == index);
        if (existing != null) TemperatureHistory = TemperatureHistory.Remove(existing);
    }

    private IDisposable SubscribeBatteryHistory(int index, TelemetryMetric metric) =>
        _batteryTelemetryClient
            .WatchBatteryHistory(index, metric, SelectedHistoryRange)
            .ToCollection()
            .ObserveOn(_synchronizationContext)
            .Subscribe(pts =>
            {
                var existing = BatteryHistory.FirstOrDefault(s => s.BatteryIndex == index && s.Metric == metric);
                var newData = new BatteryTelemetryHistorySeries { BatteryIndex = index, Metric = metric, Points = [.. pts] };
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

