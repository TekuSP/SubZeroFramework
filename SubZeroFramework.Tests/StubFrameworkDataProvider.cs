using System.Reactive.Linq;

using DynamicData;

using FrameworkDotnet.Snapshots;

using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Reusable null-object <see cref="IFrameworkDataProvider"/> for tests that need the dependency but not a
/// real EC: every stream is empty and every command echoes its request. Fan-control commands record their
/// arguments so tests can assert that a code path actuated the EC.
/// </summary>
public class StubFrameworkDataProvider : IFrameworkDataProvider
{
    public List<(int FanIndex, double DutyPercent)> SetFanDutyCalls { get; } = [];

    public List<int> RestoreAutoCalls { get; } = [];

    /// <summary>Controllable fan-state stream so tests can simulate telemetry ticks (AddOrUpdate to emit).</summary>
    public SourceCache<FanStateSnapshot, int> FanStateSource { get; } = new(static state => state.FanIndex);

    public bool IsPolling => false;

    public TimeSpan? PollingInterval => null;

    public bool IsHardwareInfoPolling => false;

    public TimeSpan? HardwareInfoPollingInterval => null;

    public IObservable<FrameworkSystemStatus> SystemStatus => Observable.Empty<FrameworkSystemStatus>();

    public IObservable<FrameworkEcFlashSnapshot> FlashSnapshots => Observable.Empty<FrameworkEcFlashSnapshot>();

    public IObservable<FrameworkFanCapabilitiesSnapshot> FanCapabilitiesSnapshots => Observable.Empty<FrameworkFanCapabilitiesSnapshot>();

    public IObservable<FrameworkPowerSnapshot> PowerSnapshots => Observable.Empty<FrameworkPowerSnapshot>();

    public IObservable<FrameworkThermalSnapshot> ThermalSnapshots => Observable.Empty<FrameworkThermalSnapshot>();

    public IObservable<HardwareInfoSnapshot> HardwareInfoSnapshots => Observable.Empty<HardwareInfoSnapshot>();

    public IObservable<IChangeSet<HistoricalRecord<FrameworkSystemStatus>, long>> ConnectSystemStatusHistory(TimeSpan historyWindow) => Observable.Empty<IChangeSet<HistoricalRecord<FrameworkSystemStatus>, long>>();

    public IObservable<IChangeSet<HistoricalRecord<FrameworkEcFlashSnapshot>, long>> ConnectFlashHistory(TimeSpan historyWindow) => Observable.Empty<IChangeSet<HistoricalRecord<FrameworkEcFlashSnapshot>, long>>();

    public IObservable<IChangeSet<HistoricalRecord<FrameworkFanCapabilitiesSnapshot>, long>> ConnectFanCapabilitiesHistory(TimeSpan historyWindow) => Observable.Empty<IChangeSet<HistoricalRecord<FrameworkFanCapabilitiesSnapshot>, long>>();

    public IObservable<IChangeSet<HistoricalRecord<FrameworkPowerSnapshot>, long>> ConnectPowerHistory(TimeSpan historyWindow) => Observable.Empty<IChangeSet<HistoricalRecord<FrameworkPowerSnapshot>, long>>();

    public IObservable<IChangeSet<HistoricalRecord<FrameworkThermalSnapshot>, long>> ConnectThermalHistory(TimeSpan historyWindow) => Observable.Empty<IChangeSet<HistoricalRecord<FrameworkThermalSnapshot>, long>>();

    public IObservable<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>> ConnectHardwareInfoHistory(TimeSpan historyWindow) => Observable.Empty<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>>();

    public IObservable<IChangeSet<FanCapabilityState, int>> ConnectFanCapabilities() => Observable.Empty<IChangeSet<FanCapabilityState, int>>();

    public IObservable<IChangeSet<FanStateSnapshot, int>> ConnectFanStates() => FanStateSource.Connect();

    public IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> ConnectTelemetryChannels() => Observable.Empty<IChangeSet<TelemetryChannel, TelemetryChannelId>>();

    public IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> ConnectCurrentTelemetryValues() => Observable.Empty<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>>();

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectTelemetrySeries(TelemetryChannelId channelId, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectTemperatureSeries(int sensorIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectFanSpeedSeries(int fanIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryChargeSeries(int batteryIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentRateSeries(int batteryIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentVoltageSeries(int batteryIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

    public bool SetPolling(TimeSpan pollingInterval) => true;

    public bool SetHardwareInfoPolling(TimeSpan pollingInterval) => true;

    public bool StartPolling() => true;

    public bool StartHardwareInfoPolling() => true;

    public bool StopPolling() => true;

    public bool StopHardwareInfoPolling() => true;

    public HardwareInfoSnapshot GetLatestHardwareInfoSnapshot() => new()
    {
        ObservedAt = DateTimeOffset.UtcNow,
        IsAvailable = false,
    };

    public void SetFanControlAuthorization(bool isFanControlEnabled, bool hasCallerIdentityValidation, string? authorizationMessage)
    {
    }

    public Task<FrameworkSystemStatus> RefreshAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new FrameworkSystemStatus
        {
            ObservedAt = DateTimeOffset.UtcNow,
            LastTelemetryObservedAt = DateTimeOffset.UtcNow,
        });

    public Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default)
        => Task.FromResult(new FrameworkFanRpmCommandResult
        {
            FanIndex = fanIndex,
            AppliedSpeedRpm = targetSpeedRpm,
        });

    public Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default)
    {
        SetFanDutyCalls.Add((fanIndex, dutyPercent));
        return Task.FromResult(new FrameworkFanDutyCommandResult
        {
            FanIndex = fanIndex,
            AppliedDutyPercent = dutyPercent,
        });
    }

    public Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, CancellationToken cancellationToken = default)
    {
        RestoreAutoCalls.Add(fanIndex);
        return Task.FromResult(new FrameworkRestoreAutoFanControlCommandResult { FanIndex = fanIndex });
    }
}
