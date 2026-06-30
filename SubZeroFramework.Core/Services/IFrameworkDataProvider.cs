using DynamicData;

using FrameworkDotnet.Snapshots;

namespace SubZeroFramework.Services;

public interface IFrameworkDataProvider
{
    bool IsPolling { get; }

    TimeSpan? PollingInterval { get; }

    bool IsHardwareInfoPolling { get; }

    TimeSpan? HardwareInfoPollingInterval { get; }

    IObservable<FrameworkSystemStatus> SystemStatus { get; }

    IObservable<FrameworkEcFlashSnapshot> FlashSnapshots { get; }

    IObservable<FrameworkFanCapabilitiesSnapshot> FanCapabilitiesSnapshots { get; }

    IObservable<FrameworkPowerSnapshot> PowerSnapshots { get; }

    /// <summary>
    /// The latest USB-C Power Delivery port state (per expansion-card slot). Empty by default; the EC-backed
    /// provider implements it from the module inventory snapshot.
    /// </summary>
    IObservable<SubZeroFramework.Models.PowerDeliverySnapshot> PowerDeliverySnapshots
        => System.Reactive.Linq.Observable.Empty<SubZeroFramework.Models.PowerDeliverySnapshot>();

    IObservable<FrameworkThermalSnapshot> ThermalSnapshots { get; }

    IObservable<IChangeSet<HistoricalRecord<FrameworkSystemStatus>, long>> ConnectSystemStatusHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkEcFlashSnapshot>, long>> ConnectFlashHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkFanCapabilitiesSnapshot>, long>> ConnectFanCapabilitiesHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkPowerSnapshot>, long>> ConnectPowerHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<HistoricalRecord<FrameworkThermalSnapshot>, long>> ConnectThermalHistory(TimeSpan historyWindow);

    IObservable<HardwareInfoSnapshot> HardwareInfoSnapshots { get; }

    IObservable<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>> ConnectHardwareInfoHistory(TimeSpan historyWindow);

    IObservable<IChangeSet<FanCapabilityState, int>> ConnectFanCapabilities();

    IObservable<IChangeSet<FanStateSnapshot, int>> ConnectFanStates();

    IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> ConnectTelemetryChannels();

    IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> ConnectCurrentTelemetryValues();

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectTelemetrySeries(TelemetryChannelId channelId, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectTemperatureSeries(int sensorIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectFanSpeedSeries(int fanIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryChargeSeries(int batteryIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentRateSeries(int batteryIndex, TimeSpan historyWindow);

    IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentVoltageSeries(int batteryIndex, TimeSpan historyWindow);

    bool SetPolling(TimeSpan pollingInterval);

    bool SetHardwareInfoPolling(TimeSpan pollingInterval);

    bool StartPolling();

    bool StartHardwareInfoPolling();

    bool StopPolling();

    bool StopHardwareInfoPolling();

    HardwareInfoSnapshot GetLatestHardwareInfoSnapshot();

    void SetFanControlAuthorization(bool isFanControlEnabled, bool hasCallerIdentityValidation, string? authorizationMessage);

    Task<FrameworkSystemStatus> RefreshAsync(CancellationToken cancellationToken = default);

    Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default);

    Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default);

    Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort restore of every fan that currently has an active manual override back to automatic EC
    /// control. Called from shutdown paths (graceful disposal and the ProcessExit hook) so fans are not left on
    /// a manual override when the service exits. No-op by default; the EC-backed provider implements it.
    /// </summary>
    void RestoreAutomaticFanControl()
    {
    }

    /// <summary>Reads the battery charge floor/ceiling from the EC, or <see langword="null"/> when unavailable.</summary>
    ChargeLimitsState? GetChargeLimits() => null;

    /// <summary>Writes the battery charge floor/ceiling to the EC. No-op by default; the EC-backed provider implements it.</summary>
    Task SetChargeLimitsAsync(int minimumPercent, int maximumPercent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
