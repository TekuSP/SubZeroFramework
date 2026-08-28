using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FrameworkDotnet.Enums;

using LiveChartsCore.Defaults;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

/// <summary>
/// The last-60-seconds sparkline behind each fan card on the Dashboard.
/// </summary>
/// <remarks>
/// <para>
/// The Fan Control page fed this history to its own <see cref="FanCardModel"/> instances and nothing else
/// did, so the Dashboard's cards had the properties the chart binds to and never any values in them. Reading
/// the same <see cref="IFanHistoryStore"/> here is what makes the two pages draw the same fan the same way
/// rather than one of them drawing nothing.
/// </para>
/// <para>
/// Its own file because it is a self-contained concern with its own subscriptions, and the page model is
/// already long.
/// </para>
/// </remarks>
public partial class DashboardModel
{
    /// <summary>
    /// How much history to ask the store for.
    /// </summary>
    /// <remarks>
    /// The SHARED constant, not a window of this page's own. EnsureFanHistory and EnsureTemperatureHistory
    /// are first-come-wins — the first caller's range is the one every later caller silently gets — and the
    /// Dashboard is the landing page, so a bespoke window here would quietly become the window the Fan
    /// Control page ends up with too.
    /// </remarks>
    private static readonly TimeSpan FanHistoryWindow = PresentationDefaults.RecentTelemetryHistoryWindow;

    /// <summary>
    /// How often the temperature history is recomputed, at most.
    /// </summary>
    /// <remarks>
    /// COALESCED, because the aggregation below walks every timestamp across every driving sensor and is
    /// therefore superlinear in the window length. Running it per telemetry sample is what made the Fan
    /// Control page's chart lag; ~3 Hz looks live and costs a fraction of it.
    /// </remarks>
    private static readonly TimeSpan TemperatureHistoryRefreshInterval = TimeSpan.FromMilliseconds(333);

    private readonly Subject<Unit> _temperatureHistoryDirty = new();

    private void AttachFanHistory()
    {
        void OnFanHistoryChanged(int fanIndex)
            => _synchronizationContext.Post(_ => RefreshFanSpeedHistory(fanIndex), null);

        void OnTemperatureHistoryChanged(int sensorIndex)
            => _temperatureHistoryDirty.OnNext(Unit.Default);

        _historyStore.FanHistoryChanged += OnFanHistoryChanged;
        _historyStore.TemperatureHistoryChanged += OnTemperatureHistoryChanged;

        Disposable
            .Create(() =>
            {
                _historyStore.FanHistoryChanged -= OnFanHistoryChanged;
                _historyStore.TemperatureHistoryChanged -= OnTemperatureHistoryChanged;
            })
            .DisposeWith(_subscriptions);

        _temperatureHistoryDirty
            .Sample(TemperatureHistoryRefreshInterval)
            .ObserveOn(_synchronizationContext)
            .Subscribe(_ => RefreshAllDrivingTemperatureHistories())
            .DisposeWith(_subscriptions);

        _temperatureHistoryDirty.DisposeWith(_subscriptions);
    }

    /// <summary>
    /// Asks the store to watch every fan on this page and every sensor those fans are driven by.
    /// </summary>
    /// <remarks>
    /// Idempotent by contract — <c>EnsureFanHistory</c> and <c>EnsureTemperatureHistory</c> are no-ops when
    /// already watching — so this can be called on every control-state change without bookkeeping here.
    /// </remarks>
    private void EnsureFanHistorySubscriptions()
    {
        foreach (var state in _fanControlStates.Values)
        {
            _historyStore.EnsureFanHistory(state.FanIndex, FanHistoryWindow);

            foreach (var sensorIndex in state.DrivingSensorIndices)
            {
                _historyStore.EnsureTemperatureHistory(sensorIndex, FanHistoryWindow);
            }
        }

        // The control state that just arrived may have changed which sensors drive a fan, which changes the
        // aggregate even when no new temperature sample has landed.
        _temperatureHistoryDirty.OnNext(Unit.Default);
    }

    private void RefreshFanSpeedHistory(int fanIndex)
    {
        if (!_fanCardsByIndex.TryGetValue(fanIndex, out var fan))
        {
            return;
        }

        if (_historyStore.GetFanHistory(fanIndex) is not { Length: > 0 } points)
        {
            fan.FanSpeedHistoryRpm = [];
            return;
        }

        // Handed over in canonical RPM. The card converts for its chart and re-derives on a unit change;
        // converting here would bake the unit into the numbers the card reasons about.
        var canonical = new DateTimePoint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            canonical[i] = new DateTimePoint(points[i].ObservedAt.LocalDateTime, points[i].SpeedRpm);
        }

        fan.FanSpeedHistoryRpm = canonical;

        // The temperature series is sampled onto THESE timestamps, so it has to be rebuilt whenever they
        // move or the two lines drift apart by one poll.
        _temperatureHistoryDirty.OnNext(Unit.Default);
    }

    private void RefreshAllDrivingTemperatureHistories()
    {
        foreach (var fan in _fanCardsByIndex.Values)
        {
            RefreshDrivingTemperatureHistory(fan);
        }
    }

    /// <summary>
    /// Reduces a fan's driving sensors to one temperature series, the way the controller reduces them to one
    /// reading.
    /// </summary>
    private void RefreshDrivingTemperatureHistory(FanCardModel fan)
    {
        if (!_fanControlStates.TryGetValue(fan.Snapshot.FanIndex, out var state)
            || state.DrivingSensorIndices.IsDefaultOrEmpty)
        {
            fan.DrivingTemperatureHistoryCelsius = [];
            return;
        }

        var perSensor = new List<TelemetryPoint[]>(state.DrivingSensorIndices.Length);
        foreach (var sensorIndex in state.DrivingSensorIndices)
        {
            if (_historyStore.TemperatureHistory.TryGetValue(sensorIndex, out var points) && points.Length > 0)
            {
                perSensor.Add(points);
            }
        }

        if (perSensor.Count == 0)
        {
            fan.DrivingTemperatureHistoryCelsius = [];
            return;
        }

        // ALIGNED TO THE FAN-SPEED SERIES, not to the sensors' own timestamps.
        //
        // Both sparkline series are plotted against sample INDEX on one shared axis, so index i has to mean
        // the same moment in both or they do not describe the same instant. Temperature history is sampled
        // less often than fan speed, so building this from the sensors' timestamps produced a shorter array —
        // and a shorter array plotted by index stops partway across the card, which reads as the temperature
        // lagging behind the RPM when it is really just a line that ran out of points.
        var timestamps = new SortedSet<DateTimeOffset>();

        if (fan.FanSpeedHistoryRpm is { Length: > 0 } speedHistory)
        {
            foreach (var point in speedHistory)
            {
                if (point.DateTime != default)
                {
                    timestamps.Add(new DateTimeOffset(point.DateTime));
                }
            }
        }
        else
        {
            // No speed history to align to yet: fall back to the sensors' own grid so the line still draws.
            foreach (var series in perSensor)
            {
                foreach (var point in series)
                {
                    timestamps.Add(point.ObservedAt);
                }
            }
        }

        var output = new List<DateTimePoint>(timestamps.Count);

        foreach (var timestamp in timestamps)
        {
            var readings = new List<double>(perSensor.Count);
            foreach (var series in perSensor)
            {
                if (TemperatureSeriesMath.FindNearestValue(series, timestamp) is double value)
                {
                    readings.Add(value);
                }
            }

            if (readings.Count == 0)
            {
                continue;
            }

            var aggregated = state.DrivingTemperatureAggregation switch
            {
                TemperatureAggregationMode.Average => readings.Average(),
                TemperatureAggregationMode.Maximum => readings.Max(),
                TemperatureAggregationMode.Minimum => readings.Min(),
                TemperatureAggregationMode.Median => TemperatureSeriesMath.Median(readings),
                _ => readings.Average(),
            };

            // Canonical Celsius; the card converts for its chart and re-derives on a unit change.
            output.Add(new DateTimePoint(timestamp.LocalDateTime, aggregated));
        }

        fan.DrivingTemperatureHistoryCelsius = [.. output];
    }
}
