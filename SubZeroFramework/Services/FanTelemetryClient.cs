using DynamicData;

using System.Reactive.Linq;

namespace SubZeroFramework.Services;

public sealed class FanTelemetryClient : IFanTelemetryClient
{
    private readonly IFrameworkTelemetryClient _frameworkTelemetryClient;
    private readonly IObservable<IChangeSet<FanTelemetrySnapshot, int>> _sharedFans;

    public FanTelemetryClient(IFrameworkTelemetryClient frameworkTelemetryClient)
    {
        ArgumentNullException.ThrowIfNull(frameworkTelemetryClient);
        _frameworkTelemetryClient = frameworkTelemetryClient;
        _sharedFans = CreateFansStream();
    }

    /// <summary>
    /// Watches the current fan telemetry set.
    /// </summary>
    public IObservable<IChangeSet<FanTelemetrySnapshot, int>> WatchFans()
    {
        return _sharedFans;
    }

    /// <summary>
    /// Watches retained fan speed history for the specified fan index and history window.
    /// </summary>
    public IObservable<IChangeSet<FanTelemetrySeriesPoint, long>> WatchFanHistory(int fanIndex, TimeSpan historyWindow)
    {
        if (fanIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fanIndex), "Fan index cannot be negative.");
        }

        var channelId = new TelemetryChannelId(
            Area: TelemetryArea.Thermal,
            EntityKind: TelemetryEntityKind.Fan,
            Index: fanIndex,
            Metric: TelemetryMetric.FanSpeedRpm);

        return _frameworkTelemetryClient
            .WatchTelemetrySeries(channelId, historyWindow)
            .Transform(point => new FanTelemetrySeriesPoint(
                SampleId: point.SampleId,
                FanIndex: point.ChannelId.Index,
                ObservedAt: point.ObservedAt,
                SpeedRpm: point.NumericValue));
    }

    private IObservable<IChangeSet<FanTelemetrySnapshot, int>> CreateFansStream()
    {
        return Observable.Create<IChangeSet<FanTelemetrySnapshot, int>>(observer =>
        {
            SourceCache<FanTelemetrySnapshot, int> fans = new(snapshot => snapshot.FanIndex);
            IDisposable cacheSubscription = fans.Connect().Subscribe(observer);
            IDisposable sourceSubscription = _frameworkTelemetryClient.WatchCurrentTelemetryValues().Subscribe(set => ApplyCurrentFanChanges(fans, set));

            return () =>
            {
                sourceSubscription.Dispose();
                cacheSubscription.Dispose();
                fans.Dispose();
            };
        });
    }

    private static void ApplyCurrentFanChanges(SourceCache<FanTelemetrySnapshot, int> fans, IChangeSet<CurrentTelemetryValue, TelemetryChannelId> set)
    {
        fans.Edit(innerCache =>
        {
            foreach (Change<CurrentTelemetryValue, TelemetryChannelId> change in set)
            {
                if (!IsFanSpeedChannel(change.Key))
                {
                    continue;
                }

                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                        innerCache.AddOrUpdate(MapSnapshot(change.Current));
                        break;
                    case ChangeReason.Remove:
                        innerCache.Remove(change.Key.Index);
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        });
    }

    private static bool IsFanSpeedChannel(TelemetryChannelId channelId)
        => channelId.Area == TelemetryArea.Thermal
            && channelId.EntityKind == TelemetryEntityKind.Fan
            && channelId.Metric == TelemetryMetric.FanSpeedRpm;

    private static FanTelemetrySnapshot MapSnapshot(CurrentTelemetryValue value)
        => new()
        {
            FanIndex = value.ChannelId.Index,
            DisplayName = value.DisplayName,
            UnitSymbol = value.UnitSymbol ?? string.Empty,
            ObservedAt = value.ObservedAt,
            SpeedRpm = value.NumericValue ?? 0,
            IsAvailable = value.IsAvailable,
        };
}
