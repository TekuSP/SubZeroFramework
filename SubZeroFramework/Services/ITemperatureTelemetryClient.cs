using DynamicData;

namespace SubZeroFramework.Services;

public interface ITemperatureTelemetryClient
{
    IObservable<IChangeSet<TemperatureTelemetrySnapshot, int>> WatchTemperatures();
    IObservable<IChangeSet<TelemetryPoint, long>> WatchTemperatureHistory(int sensorIndex, TimeSpan historyWindow);
}
