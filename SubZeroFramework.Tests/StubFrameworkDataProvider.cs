using System.Reactive.Linq;
using System.Reactive.Subjects;

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

    /// <summary>Speed commands, which is how a cascade-tracked adaptive fan is driven.</summary>
    public List<(int FanIndex, int TargetSpeedRpm)> SetFanRpmCalls { get; } = [];

    public List<int> RestoreAutoCalls { get; } = [];

    /// <summary>
    /// Makes the next N <see cref="RestoreAutoFanControlAsync"/> calls throw, so tests can exercise the
    /// retry path that keeps a fan from being stranded on an applied duty after a transient EC failure.
    /// The call is still recorded — an attempt was genuinely made.
    /// </summary>
    public int RestoreAutoFailuresRemaining { get; set; }

    /// <summary>Controllable fan-state stream so tests can simulate telemetry ticks (AddOrUpdate to emit).</summary>
    public SourceCache<FanStateSnapshot, int> FanStateSource { get; } = new(static state => state.FanIndex);

    /// <summary>
    /// Controllable thermal stream. The fan curve worker evaluates once per snapshot that survives its
    /// sampling window, so pushing here is how a test drives an evaluation.
    /// </summary>
    public Subject<FrameworkThermalSnapshot> ThermalSource { get; } = new();

    public bool IsPolling => false;

    public TimeSpan? PollingInterval => null;

    public bool IsHardwareInfoPolling => false;

    public TimeSpan? HardwareInfoPollingInterval => null;

    public IObservable<FrameworkSystemStatus> SystemStatus => Observable.Empty<FrameworkSystemStatus>();

    public IObservable<FrameworkEcFlashSnapshot> FlashSnapshots => Observable.Empty<FrameworkEcFlashSnapshot>();

    public IObservable<FrameworkFanCapabilitiesSnapshot> FanCapabilitiesSnapshots => Observable.Empty<FrameworkFanCapabilitiesSnapshot>();

    /// <summary>
    /// Controllable power stream, so a test can say whether the machine is on AC or on battery.
    /// </summary>
    /// <remarks>
    /// A test that pushes nothing here leaves the power source unknown, which consumers are expected to treat
    /// as "no information" rather than as "on battery".
    /// </remarks>
    public Subject<FrameworkPowerSnapshot> PowerSource { get; } = new();

    public IObservable<FrameworkPowerSnapshot> PowerSnapshots => PowerSource;

    public IObservable<FrameworkThermalSnapshot> ThermalSnapshots => ThermalSource;

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

    public bool SetSecondaryPolling(TimeSpan pollingInterval) => true;

    public bool SetHardwareInfoPolling(TimeSpan pollingInterval) => true;

    /// <summary>
    /// Settable so a test can hand the fan worker a CPU reading. Defaults to <see cref="ObservedControlTelemetry.None"/>,
    /// whose <see cref="DateTimeOffset.MinValue"/> timestamp reads as stale — so a test that does not set it
    /// gets "no reading" rather than a silent zero a consumer would act on.
    /// </summary>
    public ObservedControlTelemetry LatestControlTelemetry { get; set; } = ObservedControlTelemetry.None;

    public ObservedControlTelemetry GetLatestControlTelemetry() => LatestControlTelemetry;

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

    // Virtual so a test can react to a command rather than only record it. A calibration run is closed-loop —
    // it sets a duty and then measures what that duty did — so a stub that swallows commands would let the
    // run "succeed" against a machine that never responded to it.
    public virtual Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default)
    {
        SetFanRpmCalls.Add((fanIndex, targetSpeedRpm));
        return Task.FromResult(new FrameworkFanRpmCommandResult
        {
            FanIndex = fanIndex,
            AppliedSpeedRpm = targetSpeedRpm,
        });
    }

    public virtual Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default)
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

        if (RestoreAutoFailuresRemaining > 0)
        {
            RestoreAutoFailuresRemaining--;
            return Task.FromException<FrameworkRestoreAutoFanControlCommandResult>(
                new InvalidOperationException($"Simulated EC failure restoring fan {fanIndex}."));
        }

        return Task.FromResult(new FrameworkRestoreAutoFanControlCommandResult { FanIndex = fanIndex });
    }
}
