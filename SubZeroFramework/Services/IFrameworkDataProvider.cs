using DynamicData;

using FrameworkDotnet.Snapshots;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

public interface IFrameworkDataProvider : IDisposable
{
    bool IsPolling { get; }

    TimeSpan? PollingInterval { get; }

    IObservable<FrameworkSystemStatus> SystemStatus { get; }

    IObservable<FrameworkEcFlashSnapshot> FlashSnapshots { get; }

    IObservable<FrameworkFanCapabilitiesSnapshot> FanCapabilitiesSnapshots { get; }

    IObservable<FrameworkPowerSnapshot> PowerSnapshots { get; }

    IObservable<FrameworkThermalSnapshot> ThermalSnapshots { get; }

    IObservable<IChangeSet<HistoricalRecord<FrameworkSystemStatus>, long>> ConnectSystemStatusHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkEcFlashSnapshot>, long>> ConnectFlashHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkFanCapabilitiesSnapshot>, long>> ConnectFanCapabilitiesHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkPowerSnapshot>, long>> ConnectPowerHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkThermalSnapshot>, long>> ConnectThermalHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<FanCapabilityState, int>> ConnectFanCapabilities();

    IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> ConnectTelemetryChannels();

    IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> ConnectCurrentTelemetryValues();

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectTelemetrySeries(TelemetryChannelId channelId, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectTemperatureSeries(int sensorIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectFanSpeedSeries(int fanIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryChargeSeries(int batteryIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentRateSeries(int batteryIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentVoltageSeries(int batteryIndex, TimeSpan historyWindow);

    bool SetPolling(TimeSpan pollingInterval);

    bool StartPolling();

    bool StopPolling();

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
