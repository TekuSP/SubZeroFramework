using SubZeroFramework.Models;
using DynamicData;

namespace SubZeroFramework.Services;

public interface IBatteryTelemetryClient
{
    IObservable<IChangeSet<BatteryTelemetrySnapshot, int>> WatchBatteries();
    IObservable<IChangeSet<TelemetryPoint, long>> WatchBatteryHistory(int batteryIndex, TelemetryMetric metric, TimeSpan historyWindow);
}
