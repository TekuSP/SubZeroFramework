using FrameworkDotnet.Enums;

using DynamicData;

using System.Reactive.Linq;

namespace SubZeroFramework.Services;

public sealed class TemperatureTelemetryClient : ITemperatureTelemetryClient
{
    private readonly IFrameworkTelemetryClient _frameworkTelemetryClient;
    private readonly IObservable<IChangeSet<TemperatureTelemetrySnapshot, int>> _sharedSensors;

    public TemperatureTelemetryClient(IFrameworkTelemetryClient frameworkTelemetryClient)
    {
        ArgumentNullException.ThrowIfNull(frameworkTelemetryClient);
        _frameworkTelemetryClient = frameworkTelemetryClient;
        _sharedSensors = CreateSensorsStream();
    }

    public IObservable<IChangeSet<TemperatureTelemetrySnapshot, int>> WatchTemperatures()
    {
        return _sharedSensors;
    }

    public IObservable<IChangeSet<TelemetryPoint, long>> WatchTemperatureHistory(int sensorIndex, TimeSpan historyWindow)
    {
        if (sensorIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sensorIndex), "Sensor index cannot be negative.");
        }

        var channelId = new TelemetryChannelId(
            Area: TelemetryArea.Thermal,
            EntityKind: TelemetryEntityKind.TemperatureSensor,
            Index: sensorIndex,
            Metric: TelemetryMetric.TemperatureCelsius);

        return _frameworkTelemetryClient.WatchTelemetrySeries(channelId, historyWindow);
    }

    private IObservable<IChangeSet<TemperatureTelemetrySnapshot, int>> CreateSensorsStream()
    {
        return Observable.Create<IChangeSet<TemperatureTelemetrySnapshot, int>>(observer =>
        {
            SourceCache<TemperatureTelemetrySnapshot, int> cache = new(snapshot => snapshot.SensorIndex);
            var cacheSubscription = cache.Connect().Subscribe(observer);
            
            var sourceSubscription = _frameworkTelemetryClient.WatchCurrentTelemetryValues()
                .Subscribe(set => ApplyChanges(cache, set));

            return () =>
            {
                sourceSubscription.Dispose();
                cacheSubscription.Dispose();
                cache.Dispose();
            };
        });
    }

    private static void ApplyChanges(SourceCache<TemperatureTelemetrySnapshot, int> cache, IChangeSet<CurrentTelemetryValue, TelemetryChannelId> set)
    {
        cache.Edit(innerCache =>
        {
            foreach (var change in set)
            {
                if (change.Key.EntityKind != TelemetryEntityKind.TemperatureSensor)
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
                        var existingSnapshot = innerCache.Lookup(change.Key.Index);
                        var unavailableSnapshot = existingSnapshot.HasValue
                            ? existingSnapshot.Value with
                            {
                                ObservedAt = change.Current.ObservedAt,
                                IsAvailable = false,
                            }
                            : MapSnapshot(change.Current) with
                            {
                                IsAvailable = false,
                            };
                        innerCache.AddOrUpdate(unavailableSnapshot);
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        });
    }

    private static TemperatureTelemetrySnapshot MapSnapshot(CurrentTelemetryValue value)
    {
        var normalizedTemperatureState = NormalizeTemperatureState(value.TemperatureState, value.NumericValue, value.IsAvailable);

        return new TemperatureTelemetrySnapshot
        {
            SensorIndex = value.ChannelId.Index,
            DisplayName = value.DisplayName,
            UnitSymbol = value.UnitSymbol,
            ObservedAt = value.ObservedAt,
            TemperatureCelsius = value.NumericValue,
            TemperatureState = normalizedTemperatureState,
            IsAvailable = value.IsAvailable
        };
    }

    private static FrameworkTemperatureState? NormalizeTemperatureState(FrameworkTemperatureState? temperatureState, double? temperatureCelsius, bool isAvailable)
    {
        if (!isAvailable)
        {
            return temperatureState;
        }

        return temperatureCelsius == 0d && (temperatureState is null or FrameworkTemperatureState.Ok)
            ? FrameworkTemperatureState.NotPowered
            : temperatureState;
    }
}
