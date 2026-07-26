using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading;

using DynamicData;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <inheritdoc cref="IFanHistoryStore" />
public sealed class FanHistoryStore : IFanHistoryStore, IDisposable
{
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly SynchronizationContext _synchronizationContext;

    // Owns every history subscription. Per-key disposal goes through Remove(...) (disposes + removes, so the
    // composite never accumulates dead entries); Dispose() tears down whatever remains.
    private readonly CompositeDisposable _subscriptions = new();
    private readonly Dictionary<int, FanTelemetrySeriesPoint[]> _fanHistory = [];
    private readonly Dictionary<int, TelemetryPoint[]> _temperatureHistory = [];
    private readonly Dictionary<int, IDisposable> _fanSubscriptions = [];
    private readonly Dictionary<int, IDisposable> _temperatureSubscriptions = [];

    public FanHistoryStore(
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        SynchronizationContext synchronizationContext)
    {
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _synchronizationContext = synchronizationContext;
    }

    public event Action<int>? FanHistoryChanged;

    public event Action<int>? TemperatureHistoryChanged;

    public IReadOnlyDictionary<int, TelemetryPoint[]> TemperatureHistory => _temperatureHistory;

    public FanTelemetrySeriesPoint[]? GetFanHistory(int fanIndex) => _fanHistory.GetValueOrDefault(fanIndex);

    public void EnsureFanHistory(int fanIndex, TimeSpan range)
    {
        if (_fanSubscriptions.ContainsKey(fanIndex))
        {
            return;
        }

        var subscription = _fanTelemetryClient
            .WatchFanHistory(fanIndex, range)
            .Batch(TelemetryRateLimits.History)
            .ToCollection()
            .Sample(PresentationDefaults.HistoryProjectionInterval)
            // Sort and materialize BEFORE marshalling — see the temperature subscription below.
            .Select(static points => points.OrderBy(static p => p.ObservedAt).ThenBy(static p => p.SampleId).ToArray())
            .ObserveOn(_synchronizationContext)
            .Subscribe(ordered =>
            {
                _fanHistory[fanIndex] = ordered;
                FanHistoryChanged?.Invoke(fanIndex);
            })
            .DisposeWith(_subscriptions);

        _fanSubscriptions[fanIndex] = subscription;
    }

    public void EnsureTemperatureHistory(int sensorIndex, TimeSpan range)
    {
        if (_temperatureSubscriptions.ContainsKey(sensorIndex))
        {
            return;
        }

        var subscription = _temperatureTelemetryClient
            .WatchTemperatureHistory(sensorIndex, range)
            .Batch(TelemetryRateLimits.History)
            .ToCollection()
            .Sample(PresentationDefaults.HistoryProjectionInterval)
            // Sort and materialize BEFORE marshalling: everything above this line runs off the UI thread, and
            // only the assignment plus the change notification below run on it.
            .Select(static points => points.OrderBy(static p => p.ObservedAt).ThenBy(static p => p.SampleId).ToArray())
            .ObserveOn(_synchronizationContext)
            .Subscribe(ordered =>
            {
                _temperatureHistory[sensorIndex] = ordered;
                TemperatureHistoryChanged?.Invoke(sensorIndex);
            })
            .DisposeWith(_subscriptions);

        _temperatureSubscriptions[sensorIndex] = subscription;
    }

    public void StopTemperatureHistory(int sensorIndex)
    {
        if (_temperatureSubscriptions.Remove(sensorIndex, out var subscription))
        {
            subscription.Dispose();
            _subscriptions.Remove(subscription);
        }

        _temperatureHistory.Remove(sensorIndex);
    }

    public void RemoveFanHistory(int fanIndex)
    {
        if (_fanSubscriptions.Remove(fanIndex, out var subscription))
        {
            subscription.Dispose();
            _subscriptions.Remove(subscription);
        }

        _fanHistory.Remove(fanIndex);
    }

    public void Dispose()
    {
        foreach (var subscription in _fanSubscriptions.Values)
        {
            subscription.Dispose();
        }
        _fanSubscriptions.Clear();

        foreach (var subscription in _temperatureSubscriptions.Values)
        {
            subscription.Dispose();
        }
        _temperatureSubscriptions.Clear();

        _subscriptions.Dispose();
    }
}
