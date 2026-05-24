using System.Reactive.Linq;

using DynamicData;

using FrameworkDotnet.Snapshots;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Services.Hosting;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkTelemetryWorkerTests
{
    [Test]
    public async Task StartAsync_ConfiguresRefreshesAndStartsPollingLoops()
    {
        TestFrameworkDataProvider provider = new();
        TestHostApplicationLifetime applicationLifetime = new();
        using FrameworkShutdownCoordinator shutdownCoordinator = CreateShutdownCoordinator(provider);
        FrameworkServiceOptions options = new()
        {
            PollingInterval = TimeSpan.FromMilliseconds(250),
            HardwareInfoPollingInterval = TimeSpan.FromSeconds(2),
        };

        using FrameworkTelemetryWorker worker = CreateWorker(provider, applicationLifetime, shutdownCoordinator, options);

        await worker.StartAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(provider.SetPollingCalls, Is.EqualTo(1));
            Assert.That(provider.LastPollingInterval, Is.EqualTo(options.PollingInterval));
            Assert.That(provider.SetHardwareInfoPollingCalls, Is.EqualTo(1));
            Assert.That(provider.LastHardwareInfoPollingInterval, Is.EqualTo(options.HardwareInfoPollingInterval));
            Assert.That(provider.RefreshCalls, Is.EqualTo(1));
            Assert.That(provider.StartPollingCalls, Is.EqualTo(1));
            Assert.That(provider.StartHardwareInfoPollingCalls, Is.EqualTo(1));
        });

        await worker.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task StopAsync_StopsPollingLoops()
    {
        TestFrameworkDataProvider provider = new();
        TestHostApplicationLifetime applicationLifetime = new();
        using FrameworkShutdownCoordinator shutdownCoordinator = CreateShutdownCoordinator(provider);

        using FrameworkTelemetryWorker worker = CreateWorker(provider, applicationLifetime, shutdownCoordinator);

        await worker.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(provider.StopPollingCalls, Is.EqualTo(1));
            Assert.That(provider.StopHardwareInfoPollingCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplicationStopping_StopsPollingLoops()
    {
        TestFrameworkDataProvider provider = new();
        TestHostApplicationLifetime applicationLifetime = new();
        using FrameworkShutdownCoordinator shutdownCoordinator = CreateShutdownCoordinator(provider);

        using FrameworkTelemetryWorker worker = CreateWorker(provider, applicationLifetime, shutdownCoordinator);

        applicationLifetime.StopApplication();

        Assert.Multiple(() =>
        {
            Assert.That(provider.StopPollingCalls, Is.EqualTo(1));
            Assert.That(provider.StopHardwareInfoPollingCalls, Is.EqualTo(1));
        });
    }

    private static FrameworkTelemetryWorker CreateWorker(
        TestFrameworkDataProvider provider,
        TestHostApplicationLifetime applicationLifetime,
        FrameworkShutdownCoordinator shutdownCoordinator,
        FrameworkServiceOptions? options = null)
    {
        return new FrameworkTelemetryWorker(
            provider,
            Options.Create(options ?? new FrameworkServiceOptions()),
            NullLogger<FrameworkTelemetryWorker>.Instance,
            applicationLifetime,
            shutdownCoordinator);
    }

    private static FrameworkShutdownCoordinator CreateShutdownCoordinator(TestFrameworkDataProvider provider)
        => new(provider, NullLogger<FrameworkShutdownCoordinator>.Instance);

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _applicationStarted = new();
        private readonly CancellationTokenSource _applicationStopping = new();
        private readonly CancellationTokenSource _applicationStopped = new();

        public CancellationToken ApplicationStarted => _applicationStarted.Token;

        public CancellationToken ApplicationStopping => _applicationStopping.Token;

        public CancellationToken ApplicationStopped => _applicationStopped.Token;

        public void StopApplication()
        {
            if (!_applicationStopping.IsCancellationRequested)
            {
                _applicationStopping.Cancel();
            }
        }
    }

    private sealed class TestFrameworkDataProvider : IFrameworkDataProvider
    {
        public int SetPollingCalls { get; private set; }

        public TimeSpan? LastPollingInterval { get; private set; }

        public int SetHardwareInfoPollingCalls { get; private set; }

        public TimeSpan? LastHardwareInfoPollingInterval { get; private set; }

        public int RefreshCalls { get; private set; }

        public int StartPollingCalls { get; private set; }

        public int StartHardwareInfoPollingCalls { get; private set; }

        public int StopPollingCalls { get; private set; }

        public int StopHardwareInfoPollingCalls { get; private set; }

        public bool IsPolling => false;

        public TimeSpan? PollingInterval => LastPollingInterval;

        public bool IsHardwareInfoPolling => false;

        public TimeSpan? HardwareInfoPollingInterval => LastHardwareInfoPollingInterval;

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

        public IObservable<IChangeSet<FanStateSnapshot, int>> ConnectFanStates() => Observable.Empty<IChangeSet<FanStateSnapshot, int>>();

        public IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> ConnectTelemetryChannels() => Observable.Empty<IChangeSet<TelemetryChannel, TelemetryChannelId>>();

        public IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> ConnectCurrentTelemetryValues() => Observable.Empty<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>>();

        public IObservable<IChangeSet<TelemetryPoint, long>> ConnectTelemetrySeries(TelemetryChannelId channelId, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

        public IObservable<IChangeSet<TelemetryPoint, long>> ConnectTemperatureSeries(int sensorIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

        public IObservable<IChangeSet<TelemetryPoint, long>> ConnectFanSpeedSeries(int fanIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

        public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryChargeSeries(int batteryIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

        public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentRateSeries(int batteryIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

        public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentVoltageSeries(int batteryIndex, TimeSpan historyWindow) => Observable.Empty<IChangeSet<TelemetryPoint, long>>();

        public bool SetPolling(TimeSpan pollingInterval)
        {
            SetPollingCalls++;
            LastPollingInterval = pollingInterval;
            return true;
        }

        public bool SetHardwareInfoPolling(TimeSpan pollingInterval)
        {
            SetHardwareInfoPollingCalls++;
            LastHardwareInfoPollingInterval = pollingInterval;
            return true;
        }

        public bool StartPolling()
        {
            StartPollingCalls++;
            return true;
        }

        public bool StartHardwareInfoPolling()
        {
            StartHardwareInfoPollingCalls++;
            return true;
        }

        public bool StopPolling()
        {
            StopPollingCalls++;
            return true;
        }

        public bool StopHardwareInfoPolling()
        {
            StopHardwareInfoPollingCalls++;
            return true;
        }

        public HardwareInfoSnapshot GetLatestHardwareInfoSnapshot() => new()
        {
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = false,
        };

        public void SetFanControlAuthorization(bool isFanControlEnabled, bool hasCallerIdentityValidation, string? authorizationMessage)
        {
        }

        public Task<FrameworkSystemStatus> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(new FrameworkSystemStatus
            {
                ObservedAt = DateTimeOffset.UtcNow,
                IsLibraryAvailable = true,
                IsFrameworkDevice = true,
                IsEcPollingEnabled = true,
            });
        }

        public Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}