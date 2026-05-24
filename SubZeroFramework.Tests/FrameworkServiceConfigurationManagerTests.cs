using System.Reactive.Linq;

using DynamicData;

using FrameworkDotnet.Snapshots;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkServiceConfigurationManagerTests
{
    [Test]
    public async Task ApplyAsync_WhenRequestIsValid_AppliesRuntimeConfigurationWithoutPersisting()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            var (manager, provider, _, _) = CreateManager(filePath);
            using var _manager = manager;

            var result = await manager.ApplyAsync(new FrameworkServiceConfigurationApplyRequest
            {
                PollingInterval = TimeSpan.FromMilliseconds(250),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(2),
                AllowFanControlCommands = true,
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Configuration.PollingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
                Assert.That(result.Configuration.HardwareInfoPollingInterval, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(result.Configuration.AllowFanControlCommands, Is.True);
                Assert.That(provider.StopPollingCalls, Is.EqualTo(1));
                Assert.That(provider.StopHardwareInfoPollingCalls, Is.EqualTo(1));
                Assert.That(provider.SetPollingCalls, Is.EqualTo(1));
                Assert.That(provider.LastPollingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
                Assert.That(provider.SetHardwareInfoPollingCalls, Is.EqualTo(1));
                Assert.That(provider.LastHardwareInfoPollingInterval, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(provider.StartPollingCalls, Is.EqualTo(1));
                Assert.That(provider.StartHardwareInfoPollingCalls, Is.EqualTo(1));
                Assert.That(provider.RefreshCalls, Is.EqualTo(1));
                Assert.That(provider.LastFanControlEnabled, Is.True);
                Assert.That(File.Exists(filePath), Is.False, "ApplyAsync must not persist to disk.");
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task ApplyAsync_WhenIntervalsAreInvalid_ReturnsFailureWithoutApplyingChanges()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            var (manager, provider, _, _) = CreateManager(filePath);
            using var _manager = manager;

            var result = await manager.ApplyAsync(new FrameworkServiceConfigurationApplyRequest
            {
                PollingInterval = TimeSpan.Zero,
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(1),
                AllowFanControlCommands = false,
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Does.Contain("greater than zero"));
                Assert.That(provider.SetPollingCalls, Is.Zero);
                Assert.That(provider.SetHardwareInfoPollingCalls, Is.Zero);
                Assert.That(provider.StopPollingCalls, Is.Zero);
                Assert.That(provider.RefreshCalls, Is.Zero);
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task ApplyAsync_WhenApplyingFails_RollsBackRuntimeConfiguration()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            var (manager, provider, _, options) = CreateManager(filePath, providerFactory: () => new TestFrameworkDataProvider
            {
                FailSetPollingOnCallNumber = 1,
            });
            using var _manager = manager;

            var result = await manager.ApplyAsync(new FrameworkServiceConfigurationApplyRequest
            {
                PollingInterval = TimeSpan.FromMilliseconds(250),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(2),
                AllowFanControlCommands = true,
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Does.Contain("previous runtime configuration was restored"));
                Assert.That(result.Configuration.PollingInterval, Is.EqualTo(options.PollingInterval));
                Assert.That(result.Configuration.HardwareInfoPollingInterval, Is.EqualTo(options.HardwareInfoPollingInterval));
                Assert.That(result.Configuration.AllowFanControlCommands, Is.EqualTo(options.AllowFanControlCommands));
                Assert.That(provider.SetPollingCalls, Is.EqualTo(2));
                Assert.That(File.Exists(filePath), Is.False);
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task SaveAsync_PersistsCurrentConfigurationToDisk()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            var (manager, _, _, _) = CreateManager(filePath);
            using var _manager = manager;

            var apply = await manager.ApplyAsync(new FrameworkServiceConfigurationApplyRequest
            {
                PollingInterval = TimeSpan.FromMilliseconds(250),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(2),
                AllowFanControlCommands = true,
            });
            Assert.That(apply.Succeeded, Is.True);

            var saveResult = await manager.SaveAsync();

            Assert.Multiple(() =>
            {
                Assert.That(saveResult.Succeeded, Is.True);
                Assert.That(File.Exists(filePath), Is.True);
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task LoadAsync_WhenFileMissing_ReturnsFailureWithCurrentSnapshot()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            var (manager, _, _, options) = CreateManager(filePath);
            using var _manager = manager;

            var result = await manager.LoadAsync();

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Does.Contain("No persisted"));
                Assert.That(result.Configuration.PollingInterval, Is.EqualTo(options.PollingInterval));
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task LoadAsync_AfterSave_AppliesPersistedConfiguration()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            var (manager, provider, _, _) = CreateManager(filePath);
            using var _manager = manager;

            var apply = await manager.ApplyAsync(new FrameworkServiceConfigurationApplyRequest
            {
                PollingInterval = TimeSpan.FromMilliseconds(250),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(2),
                AllowFanControlCommands = true,
            });
            Assert.That(apply.Succeeded, Is.True);
            var save = await manager.SaveAsync();
            Assert.That(save.Succeeded, Is.True);

            var loadResult = await manager.LoadAsync();

            Assert.Multiple(() =>
            {
                Assert.That(loadResult.Succeeded, Is.True);
                Assert.That(loadResult.Configuration.PollingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
                Assert.That(loadResult.Configuration.HardwareInfoPollingInterval, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(loadResult.Configuration.AllowFanControlCommands, Is.True);
                Assert.That(provider.SetPollingCalls, Is.GreaterThanOrEqualTo(2));
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    private static (FrameworkServiceConfigurationManager Manager, TestFrameworkDataProvider Provider, FrameworkServiceConfigurationStore Store, FrameworkServiceOptions Options) CreateManager(
        string filePath,
        Func<TestFrameworkDataProvider>? providerFactory = null)
    {
        var provider = (providerFactory ?? (() => new TestFrameworkDataProvider()))();
        var options = new FrameworkServiceOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(150),
            HardwareInfoPollingInterval = TimeSpan.FromSeconds(1),
            AllowFanControlCommands = false,
        };
        var optionsMonitor = new TestOptionsMonitor<FrameworkServiceOptions>(options);
        var authorizationService = new FrameworkFanControlAuthorizationService(optionsMonitor, NullLogger<FrameworkFanControlAuthorizationService>.Instance);
        var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);
        var manager = new FrameworkServiceConfigurationManager(
            provider,
            authorizationService,
            optionsMonitor,
            store,
            NullLogger<FrameworkServiceConfigurationManager>.Instance);

        return (manager, provider, store, options);
    }

    private static string CreateTemporaryPath()
        => Path.Combine(Path.GetTempPath(), "SubZeroFramework.Tests", Guid.NewGuid().ToString("N"), "service-settings.json");

    private static void DeleteTemporaryPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestFrameworkDataProvider : IFrameworkDataProvider
    {
        private bool _isPolling = true;
        private bool _isHardwareInfoPolling = true;

        public int SetPollingCalls { get; private set; }

        public int SetHardwareInfoPollingCalls { get; private set; }

        public int StartPollingCalls { get; private set; }

        public int StartHardwareInfoPollingCalls { get; private set; }

        public int StopPollingCalls { get; private set; }

        public int StopHardwareInfoPollingCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public int? FailSetPollingOnCallNumber { get; init; }

        public TimeSpan? LastPollingInterval { get; private set; }

        public TimeSpan? LastHardwareInfoPollingInterval { get; private set; }

        public bool? LastFanControlEnabled { get; private set; }

        public bool? LastHasCallerIdentityValidation { get; private set; }

        public string? LastAuthorizationMessage { get; private set; }

        public bool IsPolling => _isPolling;

        public TimeSpan? PollingInterval => LastPollingInterval;

        public bool IsHardwareInfoPolling => _isHardwareInfoPolling;

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

            if (FailSetPollingOnCallNumber == SetPollingCalls)
            {
                return false;
            }

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
            _isPolling = true;
            return true;
        }

        public bool StartHardwareInfoPolling()
        {
            StartHardwareInfoPollingCalls++;
            _isHardwareInfoPolling = true;
            return true;
        }

        public bool StopPolling()
        {
            StopPollingCalls++;
            _isPolling = false;
            return true;
        }

        public bool StopHardwareInfoPolling()
        {
            StopHardwareInfoPollingCalls++;
            _isHardwareInfoPolling = false;
            return true;
        }

        public HardwareInfoSnapshot GetLatestHardwareInfoSnapshot() => new()
        {
            ObservedAt = DateTimeOffset.UtcNow,
            IsAvailable = false,
        };

        public void SetFanControlAuthorization(bool isFanControlEnabled, bool hasCallerIdentityValidation, string? authorizationMessage)
        {
            LastFanControlEnabled = isFanControlEnabled;
            LastHasCallerIdentityValidation = hasCallerIdentityValidation;
            LastAuthorizationMessage = authorizationMessage;
        }

        public Task<FrameworkSystemStatus> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(new FrameworkSystemStatus
            {
                ObservedAt = DateTimeOffset.UtcNow,
                IsGrpcActive = true,
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
