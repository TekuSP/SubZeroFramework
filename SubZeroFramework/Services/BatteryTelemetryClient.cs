using DynamicData;
using DynamicData.Kernel;

using System.Reactive.Linq;

namespace SubZeroFramework.Services;

public sealed class BatteryTelemetryClient : IBatteryTelemetryClient
{
    private readonly IFrameworkTelemetryClient _frameworkTelemetryClient;
    private readonly IObservable<IChangeSet<BatteryTelemetrySnapshot, int>> _sharedBatteries;

    public BatteryTelemetryClient(IFrameworkTelemetryClient frameworkTelemetryClient)
    {
        ArgumentNullException.ThrowIfNull(frameworkTelemetryClient);
        _frameworkTelemetryClient = frameworkTelemetryClient;
        _sharedBatteries = CreateBatteriesStream();
    }

    public IObservable<IChangeSet<BatteryTelemetrySnapshot, int>> WatchBatteries()
    {
        return _sharedBatteries;
    }

    public IObservable<IChangeSet<TelemetryPoint, long>> WatchBatteryHistory(int batteryIndex, TelemetryMetric metric, TimeSpan historyWindow)
    {
        if (batteryIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batteryIndex), "Battery index cannot be negative.");
        }

        var channelId = new TelemetryChannelId(
            Area: TelemetryArea.Power,
            EntityKind: TelemetryEntityKind.Battery,
            Index: batteryIndex,
            Metric: metric);

        return _frameworkTelemetryClient.WatchTelemetrySeries(channelId, historyWindow);
    }

    private IObservable<IChangeSet<BatteryTelemetrySnapshot, int>> CreateBatteriesStream()
    {
        return Observable.Create<IChangeSet<BatteryTelemetrySnapshot, int>>(observer =>
        {
            SourceCache<BatteryTelemetrySnapshot, int> cache = new(snapshot => snapshot.BatteryIndex);
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

    private static void ApplyChanges(SourceCache<BatteryTelemetrySnapshot, int> cache, IChangeSet<CurrentTelemetryValue, TelemetryChannelId> set)
    {
        cache.Edit(innerCache =>
        {
            foreach (var change in set)
            {
                if (change.Key.EntityKind != TelemetryEntityKind.Battery)
                {
                    continue;
                }

                int index = change.Key.Index;
                var currentSnapshot = innerCache.Lookup(index).ValueOr(() => new BatteryTelemetrySnapshot 
                { 
                    BatteryIndex = index, 
                    DisplayName = change.Current.DisplayName,
                    IsAvailable = true
                });

                var updatedSnapshot = currentSnapshot with { ObservedAt = change.Current.ObservedAt };

                updatedSnapshot = updatedSnapshot with
                {
                    DisplayName = string.IsNullOrWhiteSpace(change.Current.DisplayName) ? currentSnapshot.DisplayName : change.Current.DisplayName,
                    PowerSourceState = change.Current.PowerSourceState,
                    BatteryState = change.Current.BatteryState,
                    Manufacturer = change.Current.BatteryManufacturer,
                    ModelNumber = change.Current.BatteryModelNumber,
                    SerialNumber = change.Current.BatterySerialNumber,
                    BatteryType = change.Current.BatteryType,
                    RemainingCapacityAmpereHours = change.Current.BatteryRemainingCapacityAmpereHours,
                    DesignCapacityAmpereHours = change.Current.BatteryDesignCapacityAmpereHours,
                    LastFullChargeCapacityAmpereHours = change.Current.BatteryLastFullChargeCapacityAmpereHours,
                    DesignVoltageVolts = change.Current.BatteryDesignVoltageVolts,
                    CycleCount = change.Current.BatteryCycleCount,
                    IsAvailable = change.Current.IsAvailable,
                };

                // Map specific metrics
                if (change.Key.Metric == TelemetryMetric.BatteryChargePercent)
                    updatedSnapshot = updatedSnapshot with { ChargePercent = change.Current.NumericValue };
                else if (change.Key.Metric == TelemetryMetric.BatteryPresentVoltageVolts)
                    updatedSnapshot = updatedSnapshot with { Voltage = change.Current.NumericValue };
                else if (change.Key.Metric == TelemetryMetric.BatteryPresentRateAmperes)
                    updatedSnapshot = updatedSnapshot with { Amperage = change.Current.NumericValue };

                switch (change.Reason)
                {
                    case ChangeReason.Add:
                    case ChangeReason.Update:
                    case ChangeReason.Refresh:
                        innerCache.AddOrUpdate(updatedSnapshot);
                        break;
                    case ChangeReason.Remove:
                        innerCache.AddOrUpdate(updatedSnapshot with { IsAvailable = false });
                        break;
                    case ChangeReason.Moved:
                        break;
                }
            }
        });
    }
}
