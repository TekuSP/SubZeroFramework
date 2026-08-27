using DynamicData;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Threading;

using FrameworkDotnet;
using FrameworkDotnet.Enums;
using FrameworkDotnet.Interfaces;
using FrameworkDotnet.Responses;
using FrameworkDotnet.Snapshots;
using Hardware.Info;
using HardwareMonitor = Hardware.Info.Monitor;
using HardwareVideoController = Hardware.Info.VideoController;
using SubZeroFramework.Services.Compute;
using SubZeroFramework.Services.Control;
using SubZeroFramework.Services.Linux;
using UnitsNet;

namespace SubZeroFramework.Services;

public sealed partial class FrameworkDataProvider : IFrameworkDataProvider, IDisposable
{
    private static readonly IScheduler TelemetryScheduler = Scheduler.Default;

    private static readonly string ConnectionLibraryVersion = typeof(FrameworkSystem)
        .Assembly
        .GetName()
        .Version?
        .ToString() ?? "Unknown";

    private static readonly string? ConnectionLibraryInformationalVersion = typeof(FrameworkSystem)
        .Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion;

    private IFrameworkSystem _frameworkSystem;
    private readonly ILogger<FrameworkDataProvider> _logger;
    private readonly Lock _syncLock = new();
    private readonly RetainedSnapshotStream<FrameworkSystemStatus> _systemStatus = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<FrameworkEcFlashSnapshot> _flashSnapshots = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<FrameworkFanCapabilitiesSnapshot> _fanCapabilitiesSnapshots = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<FrameworkPowerSnapshot> _powerSnapshots = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<PowerDeliverySnapshot> _powerDeliverySnapshots = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<ModuleInventorySnapshot> _moduleInventorySnapshots = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    // The module-inventory read (USB-C PD source) is heavier than the per-fan/thermal reads and PD state changes
    // slowly (plug/unplug), so it is sampled on a calmer cadence than the main telemetry poll.
    private static readonly TimeSpan ModuleInventoryReadInterval = TimeSpan.FromSeconds(2);
    private DateTimeOffset _lastModuleInventoryReadAt = DateTimeOffset.MinValue;

    // SECONDARY tier — data the UI shows live but the fan controller does not act on, so it runs on its own
    // calmer cadence inside the primary loop rather than on every primary tick. Currently GPU/NPU utilization,
    // measured at ~1.7 ms per collect on Windows.
    //
    // The point is not the cost at today's primary interval — it is that the cost used to SCALE with it.
    // Adaptive fan control wants the primary tier as fast as it can afford, and without this split every
    // step in that direction would have multiplied the GPU sampling cost too, for numbers the UI redraws
    // about once a second regardless. Decoupling them is what makes the primary interval safe to lower.
    private static readonly TimeSpan DefaultSecondaryPollingInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _secondaryPollingInterval = DefaultSecondaryPollingInterval;

    // A DEADLINE, not a "last ran at". Re-arming from when the tier last ran would push the next run out by
    // however long that run took, so the tier would drift slower than its configured interval — and the drift
    // would be worst on the ticks that cost the most, which are exactly the ones worth sampling on time.
    private DateTimeOffset _nextSecondaryTierAt = DateTimeOffset.MinValue;

    // STATIC hardware inventory (RAM modules, drives, motherboard, BIOS, network adapters, OS identity)
    // refreshes on its own slow cadence, NOT every hardware-info poll. On Linux, Hardware.Info implements
    // the memory and drive lists by spawning `lshw` — a full device-tree probe costing hundreds of ms of
    // CPU per run — and the poll default is 1 s, which meant TWO lshw probes per second, forever. A user
    // saw exactly that as constant CPU spikes in btop (follow-up to issue #51: before lshw was a package
    // dependency the spawns failed instantly, which hid the cost). This data does not change second to
    // second; ten minutes still catches USB drives and network changes.
    //
    // The Hardware.Info poll is now the TERTIARY tier in full: nothing on it is read faster than that loop's
    // own interval. CPU usage used to be the exception that kept it fast, and it is no longer read from
    // Hardware.Info at all — see IControlTelemetryReader, which the primary loop samples directly.
    private readonly IComputeUtilizationReader _computeUtilizationReader;

    // Graphics adapters and displays for platforms Hardware.Info cannot enumerate (see the Linux branch in
    // the slow inventory tier). Refreshed on that same slow tier and cached here, because reading it walks
    // sysfs and may parse the pci.ids database — far too much work for the one-second snapshot build.
    private readonly IGraphicsInventoryReader _graphicsInventoryReader;
    private GraphicsInventory _graphicsInventory = GraphicsInventory.Empty;
    private readonly IDriveInventoryReader _driveInventoryReader;
    private DriveInventory _driveInventory = DriveInventory.Empty;
    private readonly IMemoryInventoryReader _memoryInventoryReader;
    private MemoryInventory _memoryInventory = MemoryInventory.Empty;

    // Compute-accelerator identity (the NPU's model, driver and firmware). Static, so it is resolved on the
    // slow inventory tier and cached here — the Windows resolver alone costs hundreds of milliseconds.
    private readonly IComputeDeviceIdentityResolver _computeDeviceIdentityResolver;
    private ImmutableArray<HardwareInfoComputeAccelerator> _computeAccelerators = [];
    private bool _loggedComputeAcceleratorFailure;

    /// <summary>Stable per-process channel index per compute device, keyed by its durable device key.</summary>
    private readonly Dictionary<string, int> _computeChannelIndexes = [];

    private bool _computeUtilizationFailureLogged;
    private bool _controlTelemetryFailureLogged;
    private bool _primaryOverrunLogged;
    private bool _tertiaryOverrunLogged;

    private static readonly TimeSpan StaticInventoryRefreshInterval = TimeSpan.FromMinutes(10);
    private DateTimeOffset _lastStaticInventoryRefreshAt = DateTimeOffset.MinValue;
    private readonly RetainedSnapshotStream<FrameworkThermalSnapshot> _thermalSnapshots = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    private readonly SourceCache<FanCapabilityState, int> _fanCapabilities = new(capability => capability.FanIndex);
    private readonly SourceCache<FanStateSnapshot, int> _fanStates = new(fanState => fanState.FanIndex);
    // Friendly fan names resolved from the thermal snapshot (FrameworkFanName), keyed by fan index, so the
    // capabilities stream (which has no per-fan name) reports the same label. Touched only on the telemetry scheduler.
    private readonly Dictionary<int, string> _fanDisplayNames = [];
    private readonly SourceCache<TelemetryChannel, TelemetryChannelId> _telemetryChannels = new(channel => channel.Id);
    private readonly SourceCache<CurrentTelemetryValue, TelemetryChannelId> _currentTelemetryValues = new(value => value.ChannelId);
    private readonly SourceCache<TelemetryPoint, long> _telemetryPoints = new(point => point.SampleId);
    private readonly RetainedSnapshotStream<HardwareInfoSnapshot> _hardwareInfoSnapshots = new(TelemetryHistoryLimits.MaximumHistoryWindow, TelemetryScheduler);
    private readonly CompositeDisposable _subscriptions = [];
    private readonly FrameworkFanControlSafetyTracker _fanControlSafetyTracker;
    private HardwareInfoSnapshot _latestHardwareInfoSnapshot = new()
    {
        ObservedAt = DateTimeOffset.MinValue,
        IsAvailable = false,
        LastError = "Hardware information has not been collected yet.",
    };
    private readonly IHardwareInfo _hardwareInfo;
    private readonly IHardwareInfoLogNoiseBuffer _hardwareInfoNoiseBuffer;
    private readonly IControlTelemetryReader _controlTelemetryReader;

    // The most recent primary-tier CPU reading. Held here rather than inside the hardware-info snapshot
    // because the two now live on different tiers: this is refreshed every primary tick (~150 ms) for fan
    // control, while the snapshot that carries it to the UI is rebuilt on the tertiary tier.
    private ObservedControlTelemetry _latestControlTelemetry = ObservedControlTelemetry.None;

    // Feed-forward inputs, cached at the points these subsystems already publish rather than re-read on the
    // primary tier — polling a GPU driver or the EC a second time per tick to learn something just measured
    // would be pure waste. Both are volatile: written on their own tier, read on the primary one.
    private volatile int _latestGpuPowerMilliwatts = -1;
    private volatile int _latestGpuCoreClockMegahertz = -1;

    /// <summary>Busiest GPU's utilization in tenths of a percent, or -1 when nothing reported one.</summary>
    private volatile int _latestGpuUtilizationPerMille = -1;
    private volatile int _latestSystemPowerMilliwatts = -1;

    /// <summary>
    /// When the GPU and system-power caches above were last written, as Stopwatch timestamps.
    /// </summary>
    /// <remarks>
    /// The composed control sample is stamped with the PRIMARY tier's tick time, but these fields are
    /// written on slower tiers — so a stalled GPU or power poll left a frozen value being folded into a
    /// sample that looked brand new, sailing past the consumer's freshness guard and pinning feed-forward to
    /// a reading that had stopped moving. Each cache now carries its own age and drops out when stale.
    /// </remarks>
    private long _latestGpuReadTimestamp;
    private long _latestSystemPowerTimestamp;

    /// <summary>
    /// How old a folded-in cache may be before it is reported as absent.
    /// </summary>
    /// <remarks>
    /// Generous against the tertiary tier's own interval so an ordinary slow poll does not blink the value
    /// out, and far below the ten seconds the fan worker treats as a stalled sample.
    /// </remarks>
    private static readonly TimeSpan MaximumFoldedCacheAge = TimeSpan.FromSeconds(15);

    // The most recent PD read, held so the battery publish (which runs on its own cadence) can pair charger
    // draw with charging draw. Module inventory is read far less often than power, so the two never arrive
    // together.
    private PowerDeliverySnapshot? _latestPowerDeliverySnapshot;
    private IFrameworkEcConnection? _connection;
    private TimeSpan? _pollingInterval;
    private TimeSpan? _hardwareInfoPollingInterval;
    private bool _isPolling;
    private bool _isHardwareInfoPolling;
    private CancellationTokenSource? _pollingCancellation;
    private CancellationTokenSource? _hardwareInfoPollingCancellation;
    private Task? _pollingTask;
    private Task? _hardwareInfoPollingTask;
    private long _nextTelemetryPointId;

    // Per-publish-cycle batching updaters. When set by a Publish* method that has opened a
    // SourceCache.Edit() transaction, helper methods route their mutations through the updater
    // so the entire polling cycle emits ONE consolidated ChangeSet per cache. When null,
    // helpers fall back to direct cache mutation (single-shot edits). Polling is single-threaded
    // (one telemetry polling task), so plain instance fields are sufficient.
    private ISourceUpdater<FanStateSnapshot, int>? _activeFanStatesUpdater;
    private ISourceUpdater<TelemetryChannel, TelemetryChannelId>? _activeTelemetryChannelsUpdater;
    private ISourceUpdater<CurrentTelemetryValue, TelemetryChannelId>? _activeCurrentValuesUpdater;
    private DateTimeOffset _lastTelemetryObservedAt;
    private bool _isFanControlEnabled;
    private bool _hasCallerIdentityValidation;
    private string? _fanControlAuthorizationMessage;
    private bool _disposed;

    public FrameworkDataProvider(
        IFrameworkSystem frameworkSystem,
        IHardwareInfo hardwareInfo,
        FrameworkFanControlSafetyTracker fanControlSafetyTracker,
        ILogger<FrameworkDataProvider> logger,
        IHardwareInfoLogNoiseBuffer? hardwareInfoNoiseBuffer = null,
        IComputeUtilizationReader? computeUtilizationReader = null,
        IGraphicsInventoryReader? graphicsInventoryReader = null,
        IComputeDeviceIdentityResolver? computeDeviceIdentityResolver = null,
        IDriveInventoryReader? driveInventoryReader = null,
        IMemoryInventoryReader? memoryInventoryReader = null,
        IControlTelemetryReader? controlTelemetryReader = null)
    {
        _frameworkSystem = frameworkSystem;
        _hardwareInfo = hardwareInfo;
        _fanControlSafetyTracker = fanControlSafetyTracker;
        _logger = logger;
        _hardwareInfoNoiseBuffer = hardwareInfoNoiseBuffer ?? NullHardwareInfoLogNoiseBuffer.Instance;
        // Optional by construction: a host that does not supply a reader (or a platform without one) simply
        // never publishes compute channels. GPU/NPU telemetry must never be a reason the provider fails.
        _computeUtilizationReader = computeUtilizationReader ?? UnavailableComputeUtilizationReader.Instance;
        // Same contract for graphics inventory: a platform whose display enumeration comes from Hardware.Info
        // supplies no reader here and nothing changes.
        _graphicsInventoryReader = graphicsInventoryReader ?? UnavailableGraphicsInventoryReader.Instance;
        // The same resolver the Windows utilization reader uses for LUID mapping also describes the devices,
        // so the NPU can be listed with a driver and firmware version rather than just a name and a percentage.
        _computeDeviceIdentityResolver = computeDeviceIdentityResolver ?? UnavailableComputeDeviceIdentityResolver.Instance;
        // Same contract again for drives: a platform whose drive enumeration comes from Hardware.Info supplies
        // no reader here and nothing changes.
        _driveInventoryReader = driveInventoryReader ?? UnavailableDriveInventoryReader.Instance;
        // And for memory modules, whose Linux list is both wrong (it keeps lshw's container node) and sparse.
        _memoryInventoryReader = memoryInventoryReader ?? UnavailableMemoryInventoryReader.Instance;
        // The CPU signals fan control runs on. Optional like the rest: a machine that cannot serve them simply
        // leaves the controller without feed-forward rather than failing to poll at all.
        _controlTelemetryReader = controlTelemetryReader ?? UnavailableControlTelemetryReader.Instance;
        SystemStatus = _systemStatus;
        FlashSnapshots = _flashSnapshots;
        FanCapabilitiesSnapshots = _fanCapabilitiesSnapshots;
        PowerSnapshots = _powerSnapshots;
        PowerDeliverySnapshots = _powerDeliverySnapshots;
        ModuleInventorySnapshots = _moduleInventorySnapshots;
        ThermalSnapshots = _thermalSnapshots;
        HardwareInfoSnapshots = _hardwareInfoSnapshots;
        _telemetryPoints
            .ExpireAfter(_ => TelemetryHistoryLimits.MaximumHistoryWindow, scheduler: TelemetryScheduler)
            .Subscribe()
            .DisposeWith(_subscriptions);
    }

    public bool IsPolling
    {
        get
        {
            lock (_syncLock)
            {
                return _isPolling;
            }
        }
    }

    public TimeSpan? PollingInterval
    {
        get
        {
            lock (_syncLock)
            {
                return _pollingInterval;
            }
        }
    }

    public bool IsHardwareInfoPolling
    {
        get
        {
            lock (_syncLock)
            {
                return _isHardwareInfoPolling;
            }
        }
    }

    public TimeSpan? HardwareInfoPollingInterval
    {
        get
        {
            lock (_syncLock)
            {
                return _hardwareInfoPollingInterval;
            }
        }
    }

    public IObservable<FrameworkSystemStatus> SystemStatus { get; }

    public IObservable<FrameworkEcFlashSnapshot> FlashSnapshots { get; }

    public IObservable<FrameworkFanCapabilitiesSnapshot> FanCapabilitiesSnapshots { get; }

    public IObservable<FrameworkPowerSnapshot> PowerSnapshots { get; }

    public IObservable<PowerDeliverySnapshot> PowerDeliverySnapshots { get; }

    public IObservable<ModuleInventorySnapshot> ModuleInventorySnapshots { get; }

    public IObservable<FrameworkThermalSnapshot> ThermalSnapshots { get; }

    public IObservable<HardwareInfoSnapshot> HardwareInfoSnapshots { get; }

    public void SetFanControlAuthorization(bool isFanControlEnabled, bool hasCallerIdentityValidation, string? authorizationMessage)
    {
        _isFanControlEnabled = isFanControlEnabled;
        _hasCallerIdentityValidation = hasCallerIdentityValidation;
        _fanControlAuthorizationMessage = string.IsNullOrWhiteSpace(authorizationMessage) ? null : authorizationMessage;
    }

    public IObservable<IChangeSet<HistoricalRecord<FrameworkSystemStatus>, long>> ConnectSystemStatusHistory(TimeSpan historyWindow)
        => _systemStatus.ConnectHistory(ValidateHistoryWindow(historyWindow));

    public IObservable<IChangeSet<HistoricalRecord<FrameworkEcFlashSnapshot>, long>> ConnectFlashHistory(TimeSpan historyWindow)
        => _flashSnapshots.ConnectHistory(ValidateHistoryWindow(historyWindow));

    public IObservable<IChangeSet<HistoricalRecord<FrameworkFanCapabilitiesSnapshot>, long>> ConnectFanCapabilitiesHistory(TimeSpan historyWindow)
        => _fanCapabilitiesSnapshots.ConnectHistory(ValidateHistoryWindow(historyWindow));

    public IObservable<IChangeSet<HistoricalRecord<FrameworkPowerSnapshot>, long>> ConnectPowerHistory(TimeSpan historyWindow)
        => _powerSnapshots.ConnectHistory(ValidateHistoryWindow(historyWindow));

    public IObservable<IChangeSet<HistoricalRecord<FrameworkThermalSnapshot>, long>> ConnectThermalHistory(TimeSpan historyWindow)
        => _thermalSnapshots.ConnectHistory(ValidateHistoryWindow(historyWindow));

    public IObservable<IChangeSet<HistoricalRecord<HardwareInfoSnapshot>, long>> ConnectHardwareInfoHistory(TimeSpan historyWindow)
        => _hardwareInfoSnapshots.ConnectHistory(ValidateHistoryWindow(historyWindow));

    public HardwareInfoSnapshot GetLatestHardwareInfoSnapshot()
        => _latestHardwareInfoSnapshot;

    public IObservable<IChangeSet<FanCapabilityState, int>> ConnectFanCapabilities()
        => _fanCapabilities.Connect();

    public IObservable<IChangeSet<FanStateSnapshot, int>> ConnectFanStates()
        => _fanStates.Connect();

    public IReadOnlyList<int> GetFanIndices()
        => [.. _fanStates.Keys];

    public IObservable<IChangeSet<TelemetryChannel, TelemetryChannelId>> ConnectTelemetryChannels()
        => _telemetryChannels.Connect();

    public IObservable<IChangeSet<CurrentTelemetryValue, TelemetryChannelId>> ConnectCurrentTelemetryValues()
        => _currentTelemetryValues.Connect();

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectTelemetrySeries(TelemetryChannelId channelId, TimeSpan historyWindow)
    {
        var validatedHistoryWindow = ValidateHistoryWindow(historyWindow);

        return _telemetryPoints
            .Connect()
            .Filter(point => point.ChannelId == channelId)
            .ExpireAfter(
                point =>
                {
                    var remainingLifetime = (point.ObservedAt + validatedHistoryWindow) - TelemetryScheduler.Now;
                    return remainingLifetime > TimeSpan.Zero ? remainingLifetime : TimeSpan.Zero;
                },
                scheduler: TelemetryScheduler);
    }

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectTemperatureSeries(int sensorIndex, TimeSpan historyWindow)
        => ConnectTelemetrySeries(
            new TelemetryChannelId(TelemetryArea.Thermal, TelemetryEntityKind.TemperatureSensor, sensorIndex, TelemetryMetric.TemperatureCelsius),
            historyWindow);

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectFanSpeedSeries(int fanIndex, TimeSpan historyWindow)
        => ConnectTelemetrySeries(
            new TelemetryChannelId(TelemetryArea.Thermal, TelemetryEntityKind.Fan, fanIndex, TelemetryMetric.FanSpeedRpm),
            historyWindow);

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryChargeSeries(int batteryIndex, TimeSpan historyWindow)
        => ConnectTelemetrySeries(CreateBatteryChannelId(batteryIndex, TelemetryMetric.BatteryChargePercent), historyWindow);

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentRateSeries(int batteryIndex, TimeSpan historyWindow)
        => ConnectTelemetrySeries(CreateBatteryChannelId(batteryIndex, TelemetryMetric.BatteryPresentRateAmperes), historyWindow);

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectBatteryPresentVoltageSeries(int batteryIndex, TimeSpan historyWindow)
        => ConnectTelemetrySeries(CreateBatteryChannelId(batteryIndex, TelemetryMetric.BatteryPresentVoltageVolts), historyWindow);

    public bool SetPolling(TimeSpan pollingInterval)
    {
        ThrowIfDisposed();

        if (pollingInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval), "Polling interval cannot be negative.");
        }

        var clamped = ClampTierInterval(pollingInterval, PollingTiers.Primary);

        lock (_syncLock)
        {
            if (_isPolling)
            {
                return false;
            }

            _pollingInterval = clamped;
        }

        return true;
    }

    /// <summary>
    /// Holds a tier interval inside a workable range, logging once when a configured value is moved.
    /// </summary>
    /// <remarks>
    /// These are settable from a config file and, for two of the three, over the local socket, so the bounds
    /// are what stops a typo from turning into a symptom nobody connects back to it: a primary interval of a
    /// millisecond would spin the EC read as fast as the hardware allows, and one of an hour would leave fan
    /// control acting on temperatures from another workload entirely.
    ///
    /// Clamping rather than rejecting, because refusing to start over a bad interval is a worse outcome than
    /// running at a sane one — but never silently, or the setting would appear to apply and then not.
    /// </remarks>
    private TimeSpan ClampTierInterval(TimeSpan requested, PollingTier tier)
    {
        var clamped = tier.Clamp(requested);

        if (clamped != requested)
        {
            _logger.LogWarning(
                "The {TierName} polling interval of {Requested} is outside the supported range {Minimum} to {Maximum} and has been clamped to {Clamped}.",
                tier.Name,
                requested,
                tier.Minimum,
                tier.Maximum,
                clamped);
        }

        return clamped;
    }

    /// <summary>
    /// Sets how long each tier's retained history is kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied live, with no restart: each stream's retention window is settable and shrinking one trims what
    /// it already holds. Streams are grouped by the tier that PUBLISHES them, because that is what decides
    /// how dense the history is — the EC streams at the primary interval, the inventory streams at the
    /// tertiary one.
    /// </para>
    /// <para>
    /// A non-positive value leaves that tier alone rather than emptying it, so a caller that only knows about
    /// some of the tiers cannot silently discard the others' history.
    /// </para>
    /// </remarks>
    public void SetRetention(TimeSpan primary, TimeSpan secondary, TimeSpan tertiary)
    {
        ThrowIfDisposed();

        if (primary > TimeSpan.Zero)
        {
            _systemStatus.RetentionWindow = primary;
            _flashSnapshots.RetentionWindow = primary;
            _fanCapabilitiesSnapshots.RetentionWindow = primary;
            _powerSnapshots.RetentionWindow = primary;
            _thermalSnapshots.RetentionWindow = primary;
        }

        if (secondary > TimeSpan.Zero)
        {
            _powerDeliverySnapshots.RetentionWindow = secondary;
        }

        if (tertiary > TimeSpan.Zero)
        {
            _moduleInventorySnapshots.RetentionWindow = tertiary;
            _hardwareInfoSnapshots.RetentionWindow = tertiary;
        }
    }

    /// <summary>
    /// Sets the SECONDARY tier interval. Unlike the other two this gates work INSIDE the primary loop rather
    /// than driving a loop of its own, so it can be changed while polling is running — there is no task to
    /// restart, and the next primary tick simply compares against the new interval.
    /// </summary>
    public bool SetSecondaryPolling(TimeSpan pollingInterval)
    {
        ThrowIfDisposed();

        if (pollingInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval), nameof(pollingInterval));
        }

        var clamped = ClampTierInterval(pollingInterval, PollingTiers.Secondary);

        lock (_syncLock)
        {
            _secondaryPollingInterval = clamped;
        }

        return true;
    }

    public bool SetHardwareInfoPolling(TimeSpan pollingInterval)
    {
        ThrowIfDisposed();

        if (pollingInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval), nameof(pollingInterval));
        }

        var clamped = ClampTierInterval(pollingInterval, PollingTiers.Tertiary);

        lock (_syncLock)
        {
            if (_isHardwareInfoPolling)
            {
                return false;
            }

            _hardwareInfoPollingInterval = clamped;
        }

        return true;
    }

    public bool StartPolling()
    {
        ThrowIfDisposed();

        CancellationTokenSource pollingCancellation;

        lock (_syncLock)
        {
            if (_isPolling || _pollingInterval is null || (_pollingTask is not null && !_pollingTask.IsCompleted))
            {
                return false;
            }

            _isPolling = true;
            pollingCancellation = new CancellationTokenSource();
            _pollingCancellation = pollingCancellation;
            _pollingTask = RunPollingAsync(pollingCancellation.Token);
        }

        return true;
    }

    public bool StartHardwareInfoPolling()
    {
        ThrowIfDisposed();

        CancellationTokenSource pollingCancellation;

        lock (_syncLock)
        {
            if (_isHardwareInfoPolling || _hardwareInfoPollingInterval is null || (_hardwareInfoPollingTask is not null && !_hardwareInfoPollingTask.IsCompleted))
            {
                return false;
            }

            _isHardwareInfoPolling = true;
            pollingCancellation = new CancellationTokenSource();
            _hardwareInfoPollingCancellation = pollingCancellation;
            _hardwareInfoPollingTask = RunHardwareInfoPollingAsync(pollingCancellation.Token);
        }

        return true;
    }

    public bool StopPolling()
    {
        ThrowIfDisposed();

        CancellationTokenSource? pollingCancellation;
        Task? pollingTask;

        lock (_syncLock)
        {
            if (!_isPolling && (_pollingTask is null || _pollingTask.IsCompleted))
            {
                return false;
            }

            _isPolling = false;
            pollingCancellation = _pollingCancellation;
            pollingTask = _pollingTask;
        }

        pollingCancellation?.Cancel();

        try
        {
            pollingTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            pollingCancellation?.Dispose();
        }

        var systemStatus = ReadSystemStatus();
        MarkAllTelemetryUnavailable(systemStatus.ObservedAt);
        _logger.LogDebug("Publishing system status after stopping polling. IsConnectionOpen={IsConnectionOpen}, RequiresElevation={RequiresElevation}, LastErrorPresent={HasLastError}.", systemStatus.IsConnectionOpen, systemStatus.RequiresElevation, !string.IsNullOrEmpty(systemStatus.LastError));
        _systemStatus.Publish(systemStatus, systemStatus.ObservedAt);
        return true;
    }

    public bool StopHardwareInfoPolling()
    {
        ThrowIfDisposed();

        CancellationTokenSource? pollingCancellation;
        Task? pollingTask;

        lock (_syncLock)
        {
            if (!_isHardwareInfoPolling && (_hardwareInfoPollingTask is null || _hardwareInfoPollingTask.IsCompleted))
            {
                return false;
            }

            _isHardwareInfoPolling = false;
            pollingCancellation = _hardwareInfoPollingCancellation;
            pollingTask = _hardwareInfoPollingTask;
        }

        pollingCancellation?.Cancel();

        try
        {
            pollingTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            pollingCancellation?.Dispose();
        }

        return true;
    }

    private void PublishHardwareInfoSnapshot(HardwareInfoSnapshot snapshot, DateTimeOffset observedAt)
    {
        _latestHardwareInfoSnapshot = snapshot;
        _logger.LogDebug("Publishing hardware info snapshot. IsAvailable={IsAvailable}, LastErrorPresent={HasLastError}, ObservedAt={ObservedAt}.", snapshot.IsAvailable, !string.IsNullOrEmpty(snapshot.LastError), observedAt);
        _hardwareInfoSnapshots.Publish(snapshot, observedAt);
    }

    private HardwareInfoSnapshot ReadHardwareInfoSnapshot()
    {
        var observedAt = DateTimeOffset.UtcNow;
        string? lastError = null;

        var operatingSystem = default(HardwareInfoOperatingSystem?);
        var computerSystem = default(HardwareInfoComputerSystem?);
        var motherboard = default(HardwareInfoMotherboard?);
        var bios = default(HardwareInfoBios?);
        var memoryStatus = default(HardwareInfoMemoryStatus?);
        var monitors = ImmutableArray<HardwareInfoMonitor>.Empty;
        var videoControllers = ImmutableArray<HardwareInfoVideoController>.Empty;
        var cpus = ImmutableArray<HardwareInfoCpu>.Empty;
        var memoryModules = ImmutableArray<HardwareInfoMemoryModule>.Empty;
        var drives = ImmutableArray<HardwareInfoDrive>.Empty;
        var networkAdapters = ImmutableArray<HardwareInfoNetworkAdapter>.Empty;

        void CaptureFailure(Exception exception, string operation)
        {
            _logger.LogWarning(exception, "Unable to {Operation}.", operation);
            lastError ??= exception.Message;
        }

        static ulong GetDriveFreeSpace(Drive drive)
        {
            if (drive.PartitionList is null || drive.PartitionList.Count == 0)
            {
                return 0;
            }

            ulong freeSpace = 0;

            foreach (var partition in drive.PartitionList)
            {
                if (partition.VolumeList is null)
                {
                    continue;
                }

                foreach (var volume in partition.VolumeList)
                {
                    freeSpace += volume.FreeSpace;
                }
            }

            return drive.Size == 0
                ? freeSpace
                : Math.Min(freeSpace, drive.Size);
        }

        ImmutableArray<string> ToStringArray<TValue>(System.Collections.Generic.IEnumerable<TValue>? values)
        {
            return values is null
                ? []
                : [
                    .. values
                        .Select(value => value?.ToString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                ];
        }

        // Each static-inventory probe is independent: they populate unrelated Device Capabilities
        // sections, so one throwing must not skip the rest. It used to — a single unreadable mount
        // (Hardware.Info's drive enumeration surfaces DriveNotFoundException for a mount path that
        // .NET fails to unescape from /proc/mounts) aborted the whole tier, leaving storage, network
        // AND graphics empty for a full StaticInventoryRefreshInterval with only one warning logged.
        void TryProbe(string description, Action probe)
        {
            try
            {
                probe();
            }
            catch (Exception exception)
            {
                CaptureFailure(exception, description);
            }
        }

        try
        {
            // Memory free/used is a genuinely cheap read (/proc/meminfo on Linux, a struct fill on Windows),
            // so it refreshes on every poll of this loop. Skipped Refresh* calls leave Hardware.Info's
            // previous lists in place, so the snapshot below always builds from complete (cached) inventory.
            _hardwareInfo.RefreshMemoryStatus();

            // SLOW tier — static inventory, at StaticInventoryRefreshInterval (see the field for the
            // full story: on Linux the memory/drive lists each spawn a full `lshw` probe, and running
            // that every second showed up as constant CPU spikes on a user's machine).
            if (observedAt - _lastStaticInventoryRefreshAt >= StaticInventoryRefreshInterval)
            {
                // Stamped up front: if one probe throws, retrying the whole expensive tier every second
                // until the interval elapses would reintroduce exactly the spike this exists to prevent.
                _lastStaticInventoryRefreshAt = observedAt;

                if (_memoryInventoryReader.IsAvailable)
                {
                    TryProbe("read the memory inventory", () => _memoryInventory = _memoryInventoryReader.Read());
                }
                else
                {
                    TryProbe("refresh the memory list", _hardwareInfo.RefreshMemoryList);
                }
                // Same substitution the graphics reader makes below, and for the same reason: where a platform
                // reader exists it REPLACES Hardware.Info's enumeration rather than supplementing it, so the
                // broken shell-out never runs. On Linux that also avoids the lshw probe entirely.
                if (_driveInventoryReader.IsAvailable)
                {
                    TryProbe("read the drive inventory", () => _driveInventory = _driveInventoryReader.Read());
                }
                else
                {
                    TryProbe("refresh the drive list", _hardwareInfo.RefreshDriveList);
                }
                // CPU INVENTORY only — name, core counts, caches, rated clocks. These are static, so this
                // belongs on the slow tier; it was only ever on the fast one because it doubled as the usage
                // read. includePercentProcessorTime is deliberately FALSE: passing true makes Hardware.Info
                // sample, sleep a blocking 500 ms, and sample again, which is what made a poll cost ~600 ms.
                // Usage now comes from IControlTelemetryReader on the primary tier and is merged into the
                // snapshot below.
                TryProbe("refresh the CPU list", () =>
                {
                    using var cpuCapture = _hardwareInfoNoiseBuffer.BeginCapture();
                    _hardwareInfo.RefreshCPUList(includePercentProcessorTime: false);
                    cpuCapture.SetDataPresent(_hardwareInfo.CpuList?.Count > 0);
                });
                TryProbe("refresh the motherboard list", _hardwareInfo.RefreshMotherboardList);
                TryProbe("refresh the BIOS list", _hardwareInfo.RefreshBIOSList);
                TryProbe("refresh the computer system list", _hardwareInfo.RefreshComputerSystemList);
                TryProbe("refresh the operating system information", _hardwareInfo.RefreshOperatingSystem);
                TryProbe("refresh the network adapter list", () => _hardwareInfo.RefreshNetworkAdapterList(
                    includeBytesPerSec: false,
                    includeNetworkAdapterConfiguration: true,
                    millisecondsDelayBetweenTwoMeasurements: 0));
                // Display/GPU enumeration is skipped entirely on Linux. Hardware.Info implements BOTH the
                // video-controller list ("xrandr -q") and the monitor list ("xrandr --props") by shelling
                // out to xrandr, and neither can work from here under ANY desktop stack:
                //
                //   * This process is a root systemd unit whose environment carries no DISPLAY,
                //     WAYLAND_DISPLAY or XAUTHORITY (see SubZeroFramework.Service/subzeroframework.service),
                //     so there is no display server to talk to. Verified by running xrandr with those
                //     variables stripped, WITH the package installed: it prints "Can't open display".
                //   * On a Wayland session (the reporting user runs Hyprland) xrandr can at best reach
                //     XWayland, which warns it is doing so and reports a synthetic view rather than the
                //     real outputs — so shipping the package would not have fixed it either.
                //
                // That is what IGraphicsInventoryReader now does on Linux: it reads the adapters and their
                // EDIDs straight from /sys/class/drm, which needs no display server and behaves identically
                // headless, on X11 and on Wayland. When such a reader is present it REPLACES the xrandr path
                // rather than supplementing it, so Hardware.Info's shell-outs never happen there.
                if (_graphicsInventoryReader.IsAvailable)
                {
                    TryProbe("read the graphics inventory", () => _graphicsInventory = _graphicsInventoryReader.Read());
                }
                else if (!OperatingSystem.IsLinux())
                {
                    TryProbe("refresh the video controller list", () => _hardwareInfo.RefreshVideoControllerList(refreshMonitorList: true));
                }

                TryProbe("refresh the compute accelerators", RefreshComputeAccelerators);
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "refresh hardware information");
        }

        try
        {
            if (_hardwareInfo.OperatingSystem is not null)
            {
                operatingSystem = new HardwareInfoOperatingSystem(
                    Name: _hardwareInfo.OperatingSystem.Name,
                    VersionString: _hardwareInfo.OperatingSystem.VersionString);
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read operating system data");
        }

        try
        {
            if (_hardwareInfo.ComputerSystemList.FirstOrDefault() is { } system)
            {
                computerSystem = new HardwareInfoComputerSystem(
                    Vendor: system.Vendor,
                    Caption: system.Caption,
                    Description: system.Description,
                    Name: system.Name,
                    Skunumber: system.SKUNumber,
                    Uuid: system.UUID,
                    Version: system.Version);
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read computer system data");
        }

        try
        {
            if (_hardwareInfo.MotherboardList.FirstOrDefault() is { } board)
            {
                motherboard = new HardwareInfoMotherboard(
                    Manufacturer: board.Manufacturer,
                    Product: board.Product,
                    SerialNumber: board.SerialNumber);
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read motherboard data");
        }

        try
        {
            if (_hardwareInfo.BiosList.FirstOrDefault() is { } biosSnapshot)
            {
                bios = new HardwareInfoBios(
                    Manufacturer: biosSnapshot.Manufacturer,
                    Caption: biosSnapshot.Caption,
                    Description: biosSnapshot.Description,
                    Name: biosSnapshot.Name,
                    Version: biosSnapshot.Version,
                    ReleaseDate: biosSnapshot.ReleaseDate,
                    SerialNumber: biosSnapshot.SerialNumber,
                    SoftwareElementId: biosSnapshot.SoftwareElementID);
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read BIOS data");
        }

        try
        {
            if (_hardwareInfo.MemoryStatus is not null)
            {
                memoryStatus = new HardwareInfoMemoryStatus(
                    TotalPhysical: _hardwareInfo.MemoryStatus.TotalPhysical,
                    AvailablePhysical: _hardwareInfo.MemoryStatus.AvailablePhysical,
                    TotalPageFile: _hardwareInfo.MemoryStatus.TotalPageFile,
                    AvailablePageFile: _hardwareInfo.MemoryStatus.AvailablePageFile,
                    TotalVirtual: _hardwareInfo.MemoryStatus.TotalVirtual,
                    AvailableVirtual: _hardwareInfo.MemoryStatus.AvailableVirtual,
                    AvailableExtendedVirtual: _hardwareInfo.MemoryStatus.AvailableExtendedVirtual);
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read memory status data");
        }

        var computeAccelerators = _computeAccelerators;

        // A platform-supplied inventory (Linux DRM) is authoritative where it exists: Hardware.Info's lists
        // are empty there, and re-deriving them below would only produce the same nothing.
        if (!_graphicsInventory.IsEmpty)
        {
            videoControllers = [.. _graphicsInventory.VideoControllers];
            monitors = [.. _graphicsInventory.Monitors];
        }

        try
        {
            var rawMonitors = _hardwareInfo.MonitorList.ToArray();
            var rawVideoControllers = _hardwareInfo.VideoControllerList.ToArray();
            var linkedVideoControllerDisplayNamesByMonitor = Enumerable.Range(0, rawMonitors.Length)
                .Select(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var monitorIndexByReference = new Dictionary<HardwareMonitor, int>(ReferenceEqualityComparer.Instance);

            for (var monitorIndex = 0; monitorIndex < rawMonitors.Length; monitorIndex++)
            {
                monitorIndexByReference[rawMonitors[monitorIndex]] = monitorIndex;
            }

            int FindMonitorIndex(HardwareMonitor monitor)
            {
                if (monitorIndexByReference.TryGetValue(monitor, out var directIndex))
                {
                    return directIndex;
                }

                for (var monitorIndex = 0; monitorIndex < rawMonitors.Length; monitorIndex++)
                {
                    if (MatchesMonitorIdentity(rawMonitors[monitorIndex], monitor))
                    {
                        return monitorIndex;
                    }
                }

                return -1;
            }

            if (rawVideoControllers.Length > 0)
            {
                videoControllers = rawVideoControllers
                    .Select(video =>
                    {
                        var videoControllerDisplayName = GetVideoControllerDisplayName(video);
                        var linkedMonitorDisplayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var linkedMonitor in video.MonitorList)
                        {
                            var linkedMonitorDisplayName = GetMonitorDisplayName(linkedMonitor);
                            linkedMonitorDisplayNames.Add(linkedMonitorDisplayName);

                            var monitorIndex = FindMonitorIndex(linkedMonitor);
                            if (monitorIndex >= 0)
                            {
                                linkedVideoControllerDisplayNamesByMonitor[monitorIndex].Add(videoControllerDisplayName);
                            }
                        }

                        return new HardwareInfoVideoController(
                            AdapterRAM: video.AdapterRAM,
                            Caption: video.Caption,
                            CurrentBitsPerPixel: video.CurrentBitsPerPixel,
                            CurrentHorizontalResolution: video.CurrentHorizontalResolution,
                            CurrentNumberOfColors: video.CurrentNumberOfColors,
                            CurrentRefreshRate: video.CurrentRefreshRate,
                            CurrentVerticalResolution: video.CurrentVerticalResolution,
                            Description: video.Description,
                            DriverDate: video.DriverDate,
                            DriverVersion: video.DriverVersion,
                            Manufacturer: video.Manufacturer,
                            MaxRefreshRate: video.MaxRefreshRate,
                            MinRefreshRate: video.MinRefreshRate,
                            Name: video.Name,
                            VideoModeDescription: video.VideoModeDescription,
                            VideoProcessor: video.VideoProcessor,
                            LinkedMonitorDisplayNames: [.. linkedMonitorDisplayNames]);
                    })
                    .ToImmutableArray();
            }

            if (rawMonitors.Length > 0)
            {
                monitors = rawMonitors
                    .Select((monitor, monitorIndex) => new HardwareInfoMonitor(
                        Active: monitor.Active,
                        Caption: monitor.Caption,
                        Description: monitor.Description,
                        ManufacturerName: monitor.ManufacturerName,
                        MonitorManufacturer: monitor.MonitorManufacturer,
                        MonitorType: monitor.MonitorType,
                        Name: monitor.Name,
                        PixelsPerXLogicalInch: monitor.PixelsPerXLogicalInch,
                        PixelsPerYLogicalInch: monitor.PixelsPerYLogicalInch,
                        ProductCodeId: monitor.ProductCodeID,
                        SerialNumberId: monitor.SerialNumberID,
                        UserFriendlyName: monitor.UserFriendlyName,
                        WeekOfManufacture: monitor.WeekOfManufacture,
                        YearOfManufacture: monitor.YearOfManufacture,
                        CurrentHorizontalResolution: monitor.CurrentHorizontalResolution,
                        CurrentVerticalResolution: monitor.CurrentVerticalResolution,
                        CurrentRefreshRate: monitor.CurrentRefreshRate,
                        LinkedVideoControllerDisplayNames: [.. linkedVideoControllerDisplayNamesByMonitor[monitorIndex]]))
                    .ToImmutableArray();
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read graphics and monitor data");
        }

        try
        {
            if (_hardwareInfo.CpuList is { Count: > 0 })
            {
                // Usage no longer comes from Hardware.Info — the CPU list is refreshed with measurement off,
                // so its percentages are all zero. It is merged in here from the primary tier instead.
                //
                // Only for a SINGLE-package machine, which every Framework product is. Control telemetry is
                // machine-wide, and there is no honest way to split a machine-wide figure across sockets:
                // attributing all of it to each would double count, and splitting it evenly would invent
                // detail nobody measured. On a hypothetical multi-package machine the usage fields stay null,
                // which the model already treats as "unknown" and the UI omits — rather than showing zero,
                // which would read as a genuinely idle processor.
                var controlSample = _latestControlTelemetry.Sample;
                var isSinglePackage = _hardwareInfo.CpuList.Count == 1;
                var aggregateUsagePercent = isSinglePackage && controlSample.CpuUtilizationFraction is { } aggregateFraction
                    ? Math.Clamp(aggregateFraction * 100d, 0d, 100d)
                    : (double?)null;
                var perCoreUsageFraction = isSinglePackage
                    ? controlSample.PerCoreUtilizationFraction
                    : [];

                // Package power rides the same single-package rule for the same reason: the reading is
                // machine-wide, so on a multi-package machine there is no honest way to attribute it.
                var packagePowerWatts = isSinglePackage ? controlSample.CpuPackagePowerWatts : null;

                cpus = _hardwareInfo.CpuList
                    .Select(cpu =>
                    {
                        // WMI enumerates cores in string order ("0", "1", "10", "11", … "2") — sort numerically.
                        // The control reader already orders its per-core readings the same way (group, then
                        // processor), so the two line up positionally once this sort has run.
                        var coreNames = cpu.CpuCoreList
                            .Where(core => !string.IsNullOrWhiteSpace(core.Name))
                            .Select(core => core.Name)
                            .OrderBy(ParseCpuCoreOrdinal)
                            .ThenBy(name => name, StringComparer.Ordinal)
                            .ToImmutableArray();

                        var mappedCpuCores = BuildCpuCores(coreNames, perCoreUsageFraction);

                        return new HardwareInfoCpu(
                            Name: cpu.Name ?? cpu.Caption,
                            Caption: cpu.Caption,
                            Description: cpu.Description,
                            Manufacturer: cpu.Manufacturer,
                            Cores: checked((int)cpu.NumberOfCores),
                            LogicalProcessors: checked((int)cpu.NumberOfLogicalProcessors),
                            CurrentClockSpeedMHz: checked((int)cpu.CurrentClockSpeed),
                            MaxClockSpeedMHz: checked((int)cpu.MaxClockSpeed),
                            ProcessorId: cpu.ProcessorId,
                            SocketDesignation: cpu.SocketDesignation,
                            L1CacheSizeKb: checked((int)cpu.L1InstructionCacheSize),
                            L2CacheSizeKb: checked((int)cpu.L2CacheSize),
                            L3CacheSizeKb: checked((int)cpu.L3CacheSize),
                            SecondLevelAddressTranslationExtensions: cpu.SecondLevelAddressTranslationExtensions,
                            VirtualizationFirmwareEnabled: cpu.VirtualizationFirmwareEnabled,
                            VMMonitorModeExtensions: cpu.VMMonitorModeExtensions,
                            PercentProcessorTime: aggregateUsagePercent,
                            CpuCores: mappedCpuCores,
                            PackagePowerWatts: packagePowerWatts);
                    })
                    .ToImmutableArray();
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read CPU data");
        }

        try
        {
            if (_memoryInventoryReader.IsAvailable)
            {
                memoryModules = [.. _memoryInventory.Modules];
            }
            else if (_hardwareInfo.MemoryList.Count > 0)
            {
                memoryModules = _hardwareInfo.MemoryList
                    .Select(memory => new HardwareInfoMemoryModule(
                        BankLabel: memory.BankLabel,
                        CapacityBytes: memory.Capacity,
                        DataWidth: memory.DataWidth,
                        MemoryType: memory.MemoryType.ToString(),
                        FormFactor: memory.FormFactor.ToString(),
                        SpeedMHz: memory.Speed,
                        MaxVoltage: memory.MaxVoltage,
                        MinVoltage: memory.MinVoltage,
                        Manufacturer: memory.Manufacturer,
                        PartNumber: memory.PartNumber,
                        SerialNumber: memory.SerialNumber))
                    .ToImmutableArray();
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read memory module data");
        }

        try
        {
            if (_driveInventoryReader.IsAvailable)
            {
                drives = [.. _driveInventory.Drives];
            }
            else if (_hardwareInfo.DriveList.Count > 0)
            {
                drives = _hardwareInfo.DriveList
                    .Select(drive => new HardwareInfoDrive(
                        Index: drive.Index,
                        Name: drive.Name,
                        Model: drive.Model,
                        Caption: drive.Caption,
                        Description: drive.Description,
                        Manufacturer: drive.Manufacturer,
                        MediaType: drive.MediaType,
                        SerialNumber: drive.SerialNumber,
                        FirmwareRevision: drive.FirmwareRevision,
                        Size: drive.Size,
                        FreeSpace: GetDriveFreeSpace(drive)))
                    .ToImmutableArray();
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read drive data");
        }

        try
        {
            if (_hardwareInfo.NetworkAdapterList.Count > 0)
            {
                networkAdapters = _hardwareInfo.NetworkAdapterList
                    .Select(adapter => new HardwareInfoNetworkAdapter(
                        Name: adapter.Name,
                        NetConnectionId: adapter.NetConnectionID,
                        ProductName: adapter.ProductName,
                        Caption: adapter.Caption,
                        Description: adapter.Description,
                        Manufacturer: adapter.Manufacturer,
                        AdapterType: adapter.AdapterType,
                        MacAddress: adapter.MACAddress,
                        Speed: adapter.Speed,
                        IpAddresses: ToStringArray(adapter.IPAddressList),
                        DefaultGateways: ToStringArray(adapter.DefaultIPGatewayList)))
                    .ToImmutableArray();
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read network adapter data");
        }

        return new HardwareInfoSnapshot
        {
            ObservedAt = observedAt,
            IsAvailable = lastError is null,
            LastError = lastError,
            Inventory = new HardwareInfoInventorySnapshot
            {
                OperatingSystem = operatingSystem,
                ComputerSystem = computerSystem,
                Motherboard = motherboard,
                Bios = bios,
                MemoryModules = memoryModules,
                Drives = drives,
                NetworkAdapters = networkAdapters,
                ComputeAccelerators = computeAccelerators,
            },
            Runtime = new HardwareInfoRuntimeSnapshot
            {
                MemoryStatus = memoryStatus,
                Monitors = monitors,
                VideoControllers = videoControllers,
                Cpus = cpus,
            },
        };
    }

    private async Task RunHardwareInfoPollingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var tickStartedAt = Stopwatch.GetTimestamp();

                try
                {
                    var snapshot = ReadHardwareInfoSnapshot();
                    PublishHardwareInfoSnapshot(snapshot, snapshot.ObservedAt);
                    LogHardwareInfoPollCompleted(
                        Stopwatch.GetElapsedTime(tickStartedAt).TotalMilliseconds,
                        snapshot.IsAvailable,
                        snapshot.LastError ?? "none");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "The HardwareInfo polling loop failed.");
                }

                var pollingInterval = GetHardwareInfoPollingIntervalOrDefault();
                if (pollingInterval is null)
                {
                    break;
                }

                var elapsed = Stopwatch.GetElapsedTime(tickStartedAt);
                ReportTierOverrunIfNeeded("tertiary", pollingInterval.Value, elapsed, ref _tertiaryOverrunLogged);
                await Task.Delay(PollingSchedule.ComputeDelay(pollingInterval.Value, elapsed), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_syncLock)
            {
                _isHardwareInfoPolling = false;
                _hardwareInfoPollingTask = null;
                _hardwareInfoPollingCancellation = null;
            }
        }
    }

    private TimeSpan? GetHardwareInfoPollingIntervalOrDefault()
    {
        lock (_syncLock)
        {
            return _isHardwareInfoPolling ? _hardwareInfoPollingInterval : null;
        }
    }

    public async Task<FrameworkSystemStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var systemStatus = ReadSystemStatus();

        if (!systemStatus.IsEcPollingEnabled)
        {
            DisposeConnection();
            MarkAllTelemetryUnavailable(systemStatus.ObservedAt);
            _logger.LogDebug("Publishing system status without EC polling. RequiresElevation={RequiresElevation}, LastErrorPresent={HasLastError}.", systemStatus.RequiresElevation, !string.IsNullOrEmpty(systemStatus.LastError));
            _systemStatus.Publish(systemStatus, systemStatus.ObservedAt);
            return systemStatus;
        }

        var connection = EnsureConnection();

        if (connection is null)
        {
            var unavailableStatus = systemStatus with { LastError = systemStatus.LastError ?? "Unable to open the default EC connection." };
            MarkAllTelemetryUnavailable(unavailableStatus.ObservedAt);
            _logger.LogDebug("Publishing unavailable system status because the EC connection could not be opened. LastErrorPresent={HasLastError}.", !string.IsNullOrEmpty(unavailableStatus.LastError));
            _systemStatus.Publish(unavailableStatus, unavailableStatus.ObservedAt);
            return unavailableStatus;
        }

        var observedAt = DateTimeOffset.UtcNow;
        var connectedStatus = EnrichConnectionStatus(systemStatus with { ObservedAt = observedAt }, connection);

        var refreshStartedAt = Stopwatch.GetTimestamp();
        var successfulReads = 0;
        string? snapshotError = null;

        if (TryReadSnapshot(connection.GetFlashSnapshot, "flash", ref snapshotError, out var flashSnapshot))
        {
            _logger.LogDebug("Publishing flash snapshot at {ObservedAt}.", observedAt);
            _flashSnapshots.Publish(flashSnapshot!, observedAt);
            successfulReads += 1;
        }

        if (TryReadSnapshot(connection.GetFanCapabilitiesSnapshot, "fan capabilities", ref snapshotError, out var fanCapabilitiesSnapshot))
        {
            _logger.LogDebug("Publishing fan capability snapshot for {FanCount} fan(s) at {ObservedAt}.", fanCapabilitiesSnapshot!.FanCount, observedAt);
            _fanCapabilitiesSnapshots.Publish(fanCapabilitiesSnapshot!, observedAt);
            PublishFanCapabilities(fanCapabilitiesSnapshot!, connectedStatus.Platform, connectedStatus.PlatformFamily, observedAt);
            successfulReads += 1;
        }

        if (TryReadSnapshot(connection.GetPowerSnapshot, "power", ref snapshotError, out var powerSnapshot))
        {
            // BatteryCount, not ReportedBatteries.Count(): ReportedBatteries is a Take() iterator, so counting
            // it allocated an enumerator and walked it on EVERY poll even with Debug logging switched off —
            // the argument is evaluated before the level is checked. The plain count is the same number.
            _logger.LogDebug("Publishing power snapshot for {BatteryCount} battery or batteries at {ObservedAt}.", powerSnapshot!.BatteryCount, observedAt);
            _powerSnapshots.Publish(powerSnapshot!, observedAt);
            PublishPowerTelemetry(powerSnapshot!, observedAt);
            successfulReads += 1;
        }

        if (observedAt - _lastModuleInventoryReadAt >= ModuleInventoryReadInterval
            && TryReadSnapshot(connection.GetModuleInventorySnapshot, "module inventory", ref snapshotError, out var moduleInventory))
        {
            _lastModuleInventoryReadAt = observedAt;
            FrameworkExpansionBaySnapshot? expansionBay = null;

            // FD0001 is intentionally suppressed for this call: the bay read IS constrained to Framework 16 —
            // by the runtime check on the very next line — but the analyzer recognizes only the attribute
            // chain on containing symbols, and annotating this method would falsely mark the whole
            // all-platform publish path as Framework16-only.
#pragma warning disable FD0001
            if (connectedStatus.PlatformFamily == FrameworkPlatformFamily.Framework16)
            {
                TryReadExpansionBaySnapshot(connection, ref snapshotError, out expansionBay);
            }
#pragma warning restore FD0001

            var powerDeliverySnapshot = BuildPowerDeliverySnapshot(moduleInventory!, expansionBay);
            _latestPowerDeliverySnapshot = powerDeliverySnapshot;

            _powerDeliverySnapshots.Publish(powerDeliverySnapshot, observedAt);
            _moduleInventorySnapshots.Publish(BuildModuleInventorySnapshot(moduleInventory!, expansionBay), observedAt);
            successfulReads += 1;
        }

        // PRIMARY tier — the CPU signals fan control runs on, refreshed every tick because the whole point of
        // them is to see a load spike before the temperature sensor does. Cheap by construction: cumulative
        // counters differenced in place, never a sleep (measured ~1.5-2.9 ms per tick on Windows, against the
        // ~600 ms the Hardware.Info read it replaces used to cost).
        SampleControlTelemetry(observedAt);

        // GPU/NPU telemetry, on whichever cadence is actually needed — and on NEITHER when nobody is asking.
        //
        // Two different consumers want two different things from one expensive device query. Adaptive needs
        // GPU power for its feed-forward, which is worthless stale: it exists to react to power before the
        // temperature moves, and reading it on the secondary tier meant a GPU fan anticipated heat from a
        // number up to a minute old. The charts want per-device utilisation, and are happy on the calm tier.
        //
        // So the query runs at the PRIMARY cadence when a GPU-cooled fan is being driven adaptively, and the
        // display channels are still only published on the secondary deadline. When neither is wanted it does
        // not run at all — this is a discrete GPU, and polling it holds it out of its idle power state, so
        // "nobody is looking" has to mean "do not touch it" rather than "read it anyway and discard".
        var secondaryDue = observedAt >= _nextSecondaryTierAt;
        var controlNeedsGpu = Volatile.Read(ref _gpuControlDemand) > 0;

        if (secondaryDue)
        {
            _nextSecondaryTierAt = PollingSchedule.NextDeadline(_nextSecondaryTierAt, GetSecondaryPollingInterval(), observedAt);
        }

        if (secondaryDue || controlNeedsGpu)
        {
            SampleComputeDevices(observedAt, publishChannels: secondaryDue);
        }

        if (TryReadSnapshot(connection.GetThermalSnapshot, "thermal", ref snapshotError, out var thermalSnapshot))
        {
            // FanCount rather than ReportedFans.Count() — see the power snapshot above for why.
            _logger.LogDebug("Publishing thermal snapshot for {SensorCount} sensor(s) and {FanCount} reported fan(s) at {ObservedAt}.", thermalSnapshot!.SensorCount, thermalSnapshot.FanCount, observedAt);
            _thermalSnapshots.Publish(thermalSnapshot!, observedAt);
            PublishThermalTelemetry(thermalSnapshot!, observedAt);
            successfulReads += 1;
        }

        if (successfulReads == 0)
        {
            DisposeConnection();
            MarkAllTelemetryUnavailable(observedAt);
        }

        var publishedStatus = connectedStatus with
        {
            IsConnectionOpen = successfulReads > 0,
            LastTelemetryObservedAt = successfulReads > 0 ? observedAt : connectedStatus.LastTelemetryObservedAt,
            LastError = snapshotError ?? connectedStatus.LastError,
        };

        LogEcPollCompleted(successfulReads, Stopwatch.GetElapsedTime(refreshStartedAt).TotalMilliseconds, snapshotError ?? "none");
        _logger.LogDebug("Publishing system status after refresh. IsConnectionOpen={IsConnectionOpen}, SuccessfulReads={SuccessfulReads}, LastErrorPresent={HasLastError}.", publishedStatus.IsConnectionOpen, successfulReads, !string.IsNullOrEmpty(publishedStatus.LastError));
        _systemStatus.Publish(publishedStatus, publishedStatus.ObservedAt);

        return publishedStatus;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopPollingIfRunning();
        StopHardwareInfoPolling();
        RestoreAutomaticFanControl();
        DisposeConnection();
        _subscriptions.Dispose();
        _systemStatus.Complete();
        _flashSnapshots.Complete();
        _fanCapabilitiesSnapshots.Complete();
        _powerSnapshots.Complete();
        _thermalSnapshots.Complete();
        _hardwareInfoSnapshots.Complete();
        _systemStatus.Dispose();
        _flashSnapshots.Dispose();
        _fanCapabilitiesSnapshots.Dispose();
        _powerSnapshots.Dispose();
        _thermalSnapshots.Dispose();
        _hardwareInfoSnapshots.Dispose();
        _fanCapabilities.Dispose();
        _fanStates.Dispose();
        _telemetryChannels.Dispose();
        _currentTelemetryValues.Dispose();
        _telemetryPoints.Dispose();
        _connection = null;
        _frameworkSystem = null!;
        _disposed = true;
    }

    public Task<FrameworkFanRpmCommandResult> SetFanRpmAsync(int fanIndex, int targetSpeedRpm, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (fanIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fanIndex), "Fan index cannot be negative.");
        }

        if (targetSpeedRpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSpeedRpm), "Fan RPM target must be greater than zero.");
        }

        var connection = EnsureWritableConnection();
        LogFanRpmWriteRequested(fanIndex, targetSpeedRpm);
        FrameworkSetFanRpmResponse response = connection.SetFanRpm(fanIndex, RotationalSpeed.FromRevolutionsPerMinute(targetSpeedRpm));
        _fanControlSafetyTracker.MarkOverrideActive(response.FanIndex);
        LogFanRpmWriteApplied(response.FanIndex, targetSpeedRpm, response.AppliedSpeed.RevolutionsPerMinute);

        return Task.FromResult(new FrameworkFanRpmCommandResult
        {
            FanIndex = response.FanIndex,
            AppliedSpeedRpm = checked((int)Math.Round(response.AppliedSpeed.RevolutionsPerMinute, MidpointRounding.AwayFromZero)),
        });
    }

    public Task<FrameworkFanDutyCommandResult> SetFanDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (fanIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fanIndex), "Fan index cannot be negative.");
        }

        if (double.IsNaN(dutyPercent) || double.IsInfinity(dutyPercent) || dutyPercent < 0 || dutyPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(dutyPercent), "Fan duty percent must be between 0 and 100.");
        }

        var connection = EnsureWritableConnection();

        // The EC duty register takes a whole percent; FrameworkEcConnection.SetFanDuty throws on a
        // fractional value. Curve interpolation against fractional sensor temperatures (and the CPU usage
        // boost) produces fractional duties, so round here at the single choke point before the EC write.
        var wholeDutyPercent = Math.Round(dutyPercent, MidpointRounding.AwayFromZero);
        LogFanDutyWriteRequested(fanIndex, dutyPercent, wholeDutyPercent);
        FrameworkSetFanDutyResponse response = connection.SetFanDuty(fanIndex, Ratio.FromPercent(wholeDutyPercent));
        _fanControlSafetyTracker.MarkOverrideActive(response.FanIndex);
        LogFanDutyWriteApplied(response.FanIndex, wholeDutyPercent, response.AppliedDutyCycle.Percent);

        return Task.FromResult(new FrameworkFanDutyCommandResult
        {
            FanIndex = response.FanIndex,
            AppliedDutyPercent = response.AppliedDutyCycle.Percent,
        });
    }

    public Task<FrameworkRestoreAutoFanControlCommandResult> RestoreAutoFanControlAsync(int fanIndex, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (fanIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fanIndex), "Fan index cannot be negative.");
        }

        var connection = EnsureWritableConnection();
        LogAutoFanControlRestoreRequested(fanIndex);
        FrameworkRestoreAutoFanControlResponse response = connection.RestoreAutoFanControl(fanIndex);
        _fanControlSafetyTracker.MarkAutoRestored(response.FanIndex);
        LogAutoFanControlRestored(response.FanIndex);

        return Task.FromResult(new FrameworkRestoreAutoFanControlCommandResult
        {
            FanIndex = response.FanIndex,
        });
    }

    public ChargeLimitsState? GetChargeLimits()
    {
        ThrowIfDisposed();

        var connection = EnsureConnection();
        if (connection is null)
        {
            return null;
        }

        var snapshot = connection.GetChargeLimits();
        return new ChargeLimitsState
        {
            MinimumPercent = (int)Math.Round(snapshot.MinPercent.Percent),
            MaximumPercent = (int)Math.Round(snapshot.MaxPercent.Percent),
        };
    }

    public Task SetChargeLimitsAsync(int minimumPercent, int maximumPercent, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (minimumPercent < 0 || minimumPercent > 100 || maximumPercent < 0 || maximumPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPercent), "Charge limits must be between 0 and 100 percent.");
        }

        if (minimumPercent > maximumPercent)
        {
            throw new ArgumentException("The minimum charge limit cannot exceed the maximum.", nameof(minimumPercent));
        }

        var connection = EnsureWritableConnection();
        connection.SetChargeLimits(Ratio.FromPercent(minimumPercent), Ratio.FromPercent(maximumPercent));
        return Task.CompletedTask;
    }

    private FrameworkSystemStatus ReadSystemStatus()
    {
        var lastError = default(string);
        var isLibraryAvailable = false;
        bool? isFrameworkDevice = null;
        string? deviceModel = null;
        FrameworkPlatform? platform = null;
        FrameworkPlatformFamily? platformFamily = null;
        var supportedDrivers = ImmutableArray<FrameworkEcDriver>.Empty;
        var requiresElevation = OperatingSystem.IsLinux() && !LinuxPrivilegeDetector.IsRunningAsRoot();

        try
        {
            isLibraryAvailable = _frameworkSystem.IsLibraryAvailable;
        }
        catch (Exception exception)
        {
            CaptureStatusReadFailure(exception, "evaluate Framework library availability", ref lastError);
        }

        if (isLibraryAvailable)
        {
            TryReadStatusValue(() => _frameworkSystem.IsFrameworkDevice, "detect Framework hardware", ref lastError, value => isFrameworkDevice = value);
            TryReadStatusValue(() => _frameworkSystem.GetProductName(), "read the device model", ref lastError, value => deviceModel = value);
            TryReadStatusValue(() => _frameworkSystem.GetPlatform(), "read the Framework platform", ref lastError, value => platform = value);
            TryReadStatusValue(() => _frameworkSystem.GetPlatformFamily(), "read the Framework platform family", ref lastError, value => platformFamily = value);
            supportedDrivers = ReadSupportedDrivers(ref lastError);
        }

        return new FrameworkSystemStatus
        {
            ObservedAt = DateTimeOffset.UtcNow,
            ConnectionLibraryVersion = ConnectionLibraryVersion,
            ConnectionLibraryInformationalVersion = ConnectionLibraryInformationalVersion,
            IsLibraryAvailable = isLibraryAvailable,
            IsFrameworkDevice = isFrameworkDevice,
            DeviceModel = deviceModel,
            Platform = platform,
            PlatformFamily = platformFamily,
            SupportedDrivers = supportedDrivers,
            IsEcPollingEnabled = isLibraryAvailable && isFrameworkDevice == true && !requiresElevation,
            IsConnectionOpen = false,
            IsGrpcActive = false,
            LastTelemetryObservedAt = _lastTelemetryObservedAt,
            RequiresElevation = requiresElevation,
            IsFanControlEnabled = _isFanControlEnabled,
            HasCallerIdentityValidation = _hasCallerIdentityValidation,
            FanControlAuthorizationMessage = _fanControlAuthorizationMessage,
            LastError = requiresElevation
                ? "Framework EC access on Linux requires running the service as root."
                : lastError,
        };
    }

    private IFrameworkEcConnection? EnsureConnection()
    {
        lock (_syncLock)
        {
            if (_connection is not null)
            {
                return _connection;
            }

            try
            {
                _connection = _frameworkSystem.OpenDefaultEc();
                return _connection;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Unable to open the default EC connection.");
                return null;
            }
        }
    }

    private IFrameworkEcConnection EnsureWritableConnection()
    {
        var status = ReadSystemStatus();
        if (!status.IsEcPollingEnabled)
        {
            throw new InvalidOperationException(status.LastError ?? "Framework fan control is not available in the current service state.");
        }

        return EnsureConnection()
            ?? throw new InvalidOperationException("Unable to open the default EC connection.");
    }

    private void DisposeConnection()
    {
        lock (_syncLock)
        {
            _connection?.Dispose();
            _connection = null;
            // A fresh connection gets one fresh "bay unavailable" notice if it still applies.
            _expansionBayUnavailableLogged = false;
        }
    }

    private FrameworkSystemStatus EnrichConnectionStatus(FrameworkSystemStatus systemStatus, IFrameworkEcConnection connection)
    {
        var lastError = systemStatus.LastError;
        FrameworkEcDriver? activeDriver = null;
        string? ecBuildInfo = null;

        TryReadStatusValue(connection.GetActiveDriver, "read the active EC driver", ref lastError, value => activeDriver = value);
        TryReadStatusValue(connection.GetBuildInfo, "read the EC build information", ref lastError, value => ecBuildInfo = value);

        return systemStatus with
        {
            IsConnectionOpen = true,
            IsGrpcActive = systemStatus.IsGrpcActive,
            ActiveDriver = activeDriver,
            EcBuildInfo = ecBuildInfo,
            LastError = lastError,
        };
    }

    private ImmutableArray<FrameworkEcDriver> ReadSupportedDrivers(ref string? lastError)
    {
        var supportedDrivers = ImmutableArray.CreateBuilder<FrameworkEcDriver>();

        foreach (var driver in Enum.GetValues<FrameworkEcDriver>())
        {
            if (driver == FrameworkEcDriver.Unknown)
            {
                continue;
            }

            try
            {
                if (_frameworkSystem.IsDriverSupported(driver))
                {
                    supportedDrivers.Add(driver);
                }
            }
            catch (Exception exception)
            {
                CaptureStatusReadFailure(exception, $"determine support for the {driver} driver", ref lastError);
            }
        }

        return supportedDrivers.ToImmutable();
    }

    private bool TryReadSnapshot<T>(Func<T> getSnapshot, string snapshotName, ref string? snapshotError, out T? snapshot)
    {
        try
        {
            if (!_logger.IsEnabled(LogLevel.Trace))
            {
                snapshot = getSnapshot();
                return true;
            }

            // Timing each EC read individually is the only way to tell a slow driver from a slow poll
            // interval; the stopwatch is skipped entirely unless Trace is on.
            var startedAt = Stopwatch.GetTimestamp();
            snapshot = getSnapshot();
            LogSnapshotRead(snapshotName, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to read the {SnapshotName} snapshot.", snapshotName);
            snapshotError ??= exception.Message;
            snapshot = default;
            return false;
        }
    }

    /// <summary>True once "expansion bay reports Unavailable" has been logged for the current connection.</summary>
    private bool _expansionBayUnavailableLogged;

    /// <summary>
    /// Re-resolves compute-accelerator identity. Runs on the SLOW inventory tier — the device set is static
    /// and the Windows resolver costs hundreds of milliseconds.
    /// </summary>
    /// <remarks>
    /// Only NPUs are published here: GPUs already reach the UI as video controllers, and listing them a second
    /// time under a different name would read as duplicate hardware. A resolver that reports nothing — every
    /// platform without one — simply leaves the list empty, and a failure keeps the previous list rather than
    /// blanking the NPU while the rest of the snapshot is fine.
    /// </remarks>
    private void RefreshComputeAccelerators()
    {
        try
        {
            _computeAccelerators =
            [
                .. _computeDeviceIdentityResolver
                    .Enumerate()
                    .Where(identity => identity.Kind == ComputeDeviceKind.Npu)
                    .Select(identity => new HardwareInfoComputeAccelerator(
                        DeviceKey: identity.DeviceKey,
                        Kind: identity.Kind,
                        Name: identity.DisplayName,
                        Vendor: identity.Vendor,
                        Description: identity.Description,
                        DriverName: identity.DriverName,
                        DriverVersion: identity.DriverVersion,
                        FirmwareVersion: identity.FirmwareVersion,
                        Location: identity.Location)),
            ];
        }
        catch (Exception exception)
        {
            if (!_loggedComputeAcceleratorFailure)
            {
                _loggedComputeAcceleratorFailure = true;
                _logger.LogWarning(exception, "Unable to enumerate compute accelerators; the Neural processor page will show what was last known.");
            }
        }
    }

    /// <summary>
    /// Pairs a CPU's core names with the per-core usage the primary tier measured.
    /// </summary>
    /// <remarks>
    /// Returns an EMPTY list when there is no per-core reading. <see cref="HardwareInfoCpuCore"/> cannot
    /// express "unknown" — its percentage is not nullable — so emitting names with zeroes would render as a
    /// genuinely idle core rather than an unmeasured one. An empty list is what the model already reads as
    /// no data.
    ///
    /// When the two counts disagree the MEASURED list wins and names are generated from the index: the reader
    /// enumerated the logical processors this instant, while Hardware.Info's list can be a full tertiary
    /// interval old and may not have been refreshed since a core was parked or hot-added.
    /// </remarks>
    private static ImmutableArray<HardwareInfoCpuCore> BuildCpuCores(
        ImmutableArray<string> coreNames,
        ImmutableArray<double> perCoreUsageFraction)
    {
        if (perCoreUsageFraction.IsDefaultOrEmpty)
        {
            return [];
        }

        var cores = ImmutableArray.CreateBuilder<HardwareInfoCpuCore>(perCoreUsageFraction.Length);
        for (var index = 0; index < perCoreUsageFraction.Length; index++)
        {
            cores.Add(new HardwareInfoCpuCore(
                Name: index < coreNames.Length ? coreNames[index] : index.ToString(CultureInfo.InvariantCulture),
                PercentProcessorTime: Math.Clamp(perCoreUsageFraction[index] * 100d, 0d, 100d)));
        }

        return cores.MoveToImmutable();
    }

    /// <summary>
    /// Reads the CPU signals the adaptive fan controller runs on and caches the result for
    /// <see cref="GetLatestControlTelemetry"/>.
    /// </summary>
    /// <remarks>
    /// Runs on the PRIMARY tier — every telemetry tick — because anticipating a load spike before the sensor
    /// moves is the entire reason these signals exist; sampling them slowly would defeat the purpose.
    ///
    /// A failure here must never stop a telemetry tick, so the previous sample is replaced with an empty one
    /// rather than left in place: a stale utilisation figure held indefinitely would read as a live one, and
    /// the controller would act on a number that stopped being true minutes ago.
    /// </remarks>
    /// <summary>Total GPU power across every graphics device, or null when nothing reported it.</summary>
    private double? ReadTotalGpuPowerWatts()
    {
        var milliwatts = _latestGpuPowerMilliwatts;
        return milliwatts >= 0 && IsFresh(_latestGpuReadTimestamp) ? milliwatts / 1000d : null;
    }

    /// <summary>Whether a cache written on a slower tier is recent enough to fold into a control sample.</summary>
    private static bool IsFresh(long timestamp)
    {
        var taken = Volatile.Read(ref timestamp);
        return taken != 0L && Stopwatch.GetElapsedTime(taken) <= MaximumFoldedCacheAge;
    }

    /// <summary>The fastest graphics core clock reported, or null when nothing reported one.</summary>
    private double? ReadGpuCoreClockMegahertz()
    {
        var megahertz = _latestGpuCoreClockMegahertz;
        return megahertz > 0 && IsFresh(_latestGpuReadTimestamp) ? megahertz : null;
    }

    /// <summary>The busiest GPU's busy share as 0–1, or null when nothing reported one.</summary>
    private double? ReadGpuUtilizationFraction()
    {
        var perMille = _latestGpuUtilizationPerMille;
        return perMille >= 0 && IsFresh(_latestGpuReadTimestamp) ? perMille / 1000d : null;
    }

    /// <summary>System draw derived from the charger, or null when running on battery.</summary>
    private double? ReadSystemPowerWatts()
    {
        var milliwatts = _latestSystemPowerMilliwatts;
        return milliwatts >= 0 && IsFresh(_latestSystemPowerTimestamp) ? milliwatts / 1000d : null;
    }

    /// <summary>
    /// Caches total system draw — the same physical quantity whichever way the machine is powered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On AC that is charger draw LESS whatever is going into the battery. The subtraction matters: charging
    /// can pull 60 W that becomes chemistry rather than heat in the fan's zone, so treating raw adapter draw
    /// as load would have the fans spin hardest on a cold machine that was simply plugged in — the opposite
    /// of what the user expects, and worst on a laptop just opened at a desk.
    /// </para>
    /// <para>
    /// On battery it is the discharge rate, which measures exactly the same thing from the other side. Without
    /// this half a Windows laptop has NO usable feed-forward signal off the charger — no package power, no
    /// adapter — so a mostly-portable machine would never gather the evidence to identify its own model and
    /// would run on conservative defaults forever.
    /// </para>
    /// <para>
    /// One caveat worth knowing: on AC, charging also warms the pack and the charging circuitry inside the
    /// chassis, and that heat is real but is deliberately NOT in this figure. It leaves a small systematic
    /// difference between the AC and battery couplings, which the identified intercept partly absorbs. If it
    /// ever proves to matter, the fix is a per-supply coefficient, not abandoning the subtraction.
    /// </para>
    /// </remarks>
    private void CacheSystemPower(FrameworkPowerSnapshot powerSnapshot, PowerDeliverySnapshot powerDelivery)
    {
        var adapterWatts = 0d;
        var sawAdapter = false;

        foreach (var port in powerDelivery.Ports)
        {
            // Only a port actually sinking power is an input; a port powering a peripheral is a load we are
            // not trying to anticipate.
            if (!port.IsPresent || !port.HasPowerDeliveryContract)
            {
                continue;
            }

            var watts = port.VoltageVolts * port.CurrentAmperes;
            if (double.IsFinite(watts) && watts > 0d)
            {
                adapterWatts += watts;
                sawAdapter = true;
            }
        }

        _latestSystemPowerMilliwatts = sawAdapter
            ? ToMilliwatts(adapterWatts - TotalBatteryChargeWatts(powerSnapshot))
            : ToMilliwatts(TotalBatteryDischargeWatts(powerSnapshot));
        Volatile.Write(ref _latestSystemPowerTimestamp, Stopwatch.GetTimestamp());
    }

    /// <summary>Power flowing INTO the batteries, in watts. Zero when nothing is charging.</summary>
    private static double TotalBatteryChargeWatts(FrameworkPowerSnapshot powerSnapshot)
        => SumBatteryWatts(powerSnapshot, FrameworkBatteryState.Charging);

    /// <summary>Power flowing OUT of the batteries, in watts — system draw when off the charger.</summary>
    private static double TotalBatteryDischargeWatts(FrameworkPowerSnapshot powerSnapshot)
        => SumBatteryWatts(powerSnapshot, FrameworkBatteryState.Discharging);

    private static double SumBatteryWatts(FrameworkPowerSnapshot powerSnapshot, FrameworkBatteryState state)
    {
        var total = 0d;

        foreach (var battery in powerSnapshot.ReportedBatteries)
        {
            if (battery.BatteryState != state)
            {
                continue;
            }

            // PresentRate is reported as a magnitude; the state is what carries the direction.
            var watts = battery.PresentVoltage.Volts * Math.Abs(battery.PresentRate.Amperes);
            if (double.IsFinite(watts) && watts > 0d)
            {
                total += watts;
            }
        }

        return total;
    }

    private static int ToMilliwatts(double watts)
        => double.IsFinite(watts) && watts > 0d ? (int)Math.Round(watts * 1000d) : -1;

    private void SampleControlTelemetry(DateTimeOffset observedAt)
    {
        if (!_controlTelemetryReader.IsAvailable)
        {
            return;
        }

        try
        {
            // The reader knows CPU signals only. Adapter and GPU power come from subsystems this provider is
            // already polling, so they are folded in here rather than pushed into the reader abstraction —
            // which would force every platform reader to learn about chargers and graphics drivers.
            var sample = _controlTelemetryReader.Sample() with
            {
                GpuPowerWatts = ReadTotalGpuPowerWatts(),
                GpuCoreClockMegahertz = ReadGpuCoreClockMegahertz(),
                GpuUtilizationFraction = ReadGpuUtilizationFraction(),
                SystemPowerWatts = ReadSystemPowerWatts(),
            };

            _latestControlTelemetry = new ObservedControlTelemetry(sample, observedAt);
        }
        catch (Exception exception)
        {
            _latestControlTelemetry = ObservedControlTelemetry.None;

            if (!_controlTelemetryFailureLogged)
            {
                _controlTelemetryFailureLogged = true;
                _logger.LogWarning(exception, "Unable to read CPU control telemetry; adaptive fan control will run without CPU signals.");
            }
        }
    }

    /// <summary>
    /// The most recent primary-tier CPU reading, with the time it was taken. Consumers that drive fan control
    /// read this rather than the hardware-info snapshot, which is rebuilt on the tertiary tier and would be up
    /// to that interval old.
    /// </summary>
    public ObservedControlTelemetry GetLatestControlTelemetry() => _latestControlTelemetry;

    private TimeSpan GetSecondaryPollingInterval()
    {
        lock (_syncLock)
        {
            return _secondaryPollingInterval;
        }
    }

    /// <summary>
    /// Publishes one utilization channel per GPU / NPU the reader can see. Runs on the SECONDARY tier — this
    /// is display data, and the Windows PDH reader costs ~1.7 ms per collect (measured), which is affordable
    /// once a second and wasteful at the primary tick rate. Devices are published individually — never
    /// blended — and a device the reader stops seeing goes unavailable rather than freezing at its last value.
    /// </summary>
    /// <summary>
    /// How many callers currently need GPU power on the control cadence. Zero means the device is left alone.
    /// </summary>
    /// <remarks>
    /// A count rather than a flag: two adaptive GPU fans, or a fan plus a running calibration, each want it,
    /// and the last one to finish is the one that may let the device go back to sleep.
    /// </remarks>
    private int _gpuControlDemand;

    /// <inheritdoc />
    public IDisposable RequireGpuControlTelemetry()
    {
        ThrowIfDisposed();

        Interlocked.Increment(ref _gpuControlDemand);
        return new GpuControlDemandLease(this);
    }

    /// <summary>Releases one caller's claim; the device stops being polled when the last one goes.</summary>
    private sealed class GpuControlDemandLease(FrameworkDataProvider owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            // Guarded, because disposing a lease twice would drop the count below the number of real holders
            // and stop polling for whoever is still using it.
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref owner._gpuControlDemand);
            }
        }
    }

    /// <param name="publishChannels">
    /// False when this read exists only to refresh the control figures. The per-device channels are the
    /// secondary tier's output, and republishing them at the primary cadence would push history into the
    /// charts far faster than the user asked them to move.
    /// </param>
    private void SampleComputeDevices(DateTimeOffset observedAt, bool publishChannels)
    {
        if (!_computeUtilizationReader.IsAvailable)
        {
            return;
        }

        IReadOnlyList<ComputeDeviceUtilization> devices;
        try
        {
            devices = _computeUtilizationReader.Sample();
        }
        catch (Exception exception)
        {
            // A telemetry tick must survive a misbehaving counter/driver. Log once, then stay quiet.
            if (!_computeUtilizationFailureLogged)
            {
                _computeUtilizationFailureLogged = true;
                _logger.LogWarning(exception, "Reading GPU/NPU utilization failed; the devices will show as unavailable. Further failures are suppressed.");
            }

            return;
        }

        // Every GPU's power, summed: a Framework 16 with the graphics module has two, and the fan is cooling
        // the chassis both sit in. NPUs are excluded — they are a rounding error thermally and reporting them
        // as GPU load would bias the model on machines that have one.
        var gpuPowerWatts = 0d;
        var sawGpuPower = false;
        foreach (var candidate in devices)
        {
            if (candidate.Kind != ComputeDeviceKind.Npu && candidate.PowerWatts is double watts && double.IsFinite(watts) && watts >= 0d)
            {
                gpuPowerWatts += watts;
                sawGpuPower = true;
            }
        }

        _latestGpuPowerMilliwatts = sawGpuPower ? (int)Math.Round(gpuPowerWatts * 1000d) : -1;

        // One stamp for the whole GPU read: power, clock and utilisation are all written by this pass.
        Volatile.Write(ref _latestGpuReadTimestamp, Stopwatch.GetTimestamp());

        // The HIGHEST core clock rather than the sum: clocks do not add. This is the speed the busiest
        // graphics device is actually sustaining, which is what a calibration compares across fan duties to
        // answer "what does more fan buy?".
        var fastestClock = -1;
        foreach (var candidate in devices)
        {
            if (candidate.Kind != ComputeDeviceKind.Npu
                && candidate.CoreClockMegahertz is double megahertz
                && double.IsFinite(megahertz)
                && megahertz > 0d)
            {
                fastestClock = Math.Max(fastestClock, (int)Math.Round(megahertz));
            }
        }

        _latestGpuCoreClockMegahertz = fastestClock;

        // The BUSIEST device, not a sum — busy shares do not add, and during a GPU-load calibration the
        // loaded device is by definition the busiest one. Tenths of a percent so the volatile stays an int.
        var busiestPerMille = -1;
        foreach (var candidate in devices)
        {
            if (candidate.Kind != ComputeDeviceKind.Npu
                && double.IsFinite(candidate.UtilizationPercent)
                && candidate.UtilizationPercent >= 0d)
            {
                busiestPerMille = Math.Max(busiestPerMille, (int)Math.Round(candidate.UtilizationPercent * 10d));
            }
        }

        _latestGpuUtilizationPerMille = busiestPerMille;

        // The control figures are extracted above and are always refreshed. Everything below is the display
        // side — per-device channels and their history — which belongs to the secondary tier alone.
        if (!publishChannels)
        {
            return;
        }

        var observedGpuChannels = new HashSet<TelemetryChannelId>();
        var observedNpuChannels = new HashSet<TelemetryChannelId>();

        foreach (var device in devices)
        {
            var entityKind = device.Kind == ComputeDeviceKind.Npu ? TelemetryEntityKind.Npu : TelemetryEntityKind.Gpu;

            // The channel index must be stable for the life of the process so a device keeps its identity in
            // the UI. DeviceKey is the durable id (PCI path / device instance path); the index is just its
            // first-seen slot, assigned once and never reused.
            var index = GetComputeChannelIndex(device.DeviceKey);
            var channelId = new TelemetryChannelId(
                Area: TelemetryArea.Compute,
                EntityKind: entityKind,
                Index: index,
                Metric: TelemetryMetric.UtilizationPercent);

            var observedChannels = entityKind == TelemetryEntityKind.Npu ? observedNpuChannels : observedGpuChannels;
            observedChannels.Add(channelId);

            PublishNumericTelemetry(
                channelId: channelId,
                displayName: device.DisplayName,
                unitSymbol: "%",
                observedAt: observedAt,
                numericValue: device.UtilizationPercent,
                computeDevice: device);

            // Video memory as its OWN channel, so the service retains its history the same way it retains
            // every other series and the UI can chart it without accumulating anything client-side.
            //
            // Published only when the device reports both halves. A device that has no dedicated video
            // memory (an integrated GPU sharing system RAM) gets no channel at all rather than a zero that
            // would chart as "0% used" — and because the channel is then absent from the observed set,
            // SetChannelsAvailability marks it unavailable and the card hides the chart.
            if (device.VramUtilizationPercent is { } vramPercent)
            {
                var vramChannelId = channelId with { Metric = TelemetryMetric.VramUtilizationPercent };
                observedChannels.Add(vramChannelId);

                PublishNumericTelemetry(
                    channelId: vramChannelId,
                    displayName: device.DisplayName,
                    unitSymbol: "%",
                    observedAt: observedAt,
                    numericValue: vramPercent,
                    computeDevice: device);
            }
        }

        SetChannelsAvailability(TelemetryArea.Compute, TelemetryEntityKind.Gpu, observedGpuChannels, observedAt);
        SetChannelsAvailability(TelemetryArea.Compute, TelemetryEntityKind.Npu, observedNpuChannels, observedAt);
    }

    private int GetComputeChannelIndex(string deviceKey)
    {
        if (_computeChannelIndexes.TryGetValue(deviceKey, out var index))
        {
            return index;
        }

        index = _computeChannelIndexes.Count;
        _computeChannelIndexes[deviceKey] = index;
        return index;
    }

    /// <summary>
    /// Reads the Framework 16 expansion-bay snapshot, treating an EC "Unavailable" response as an EMPTY
    /// BAY rather than as a failure.
    /// </summary>
    /// <remarks>
    /// The expansion bay is OPTIONAL hardware, and on FW16 configurations where the bay does not report
    /// the EC answers <c>Unavailable (9)</c> on every poll. Routing that through
    /// <see cref="TryReadSnapshot{T}"/> put its message into <c>FrameworkSystemStatus.LastError</c> each
    /// cycle — and the client treats ANY non-empty LastError as unhealthy, so the whole app locked into
    /// the recovery page on a machine whose fans and thermals were reading perfectly (first field
    /// report: issue #51).
    ///
    /// An unavailable optional module is data, not an error, so a synthesized snapshot with
    /// <see cref="FrameworkExpansionBayBoard.NoModule"/> is returned instead: the module inventory then
    /// deliberately shows "empty bay" (NoModule) rather than "could not read" (the Unknown fallback a
    /// null snapshot would produce), no phantom module appears (IsPresent=false ⇒ Identity=None, so the
    /// identity overlay and the bay USB-C port are both skipped), and LastError stays clean. Any OTHER
    /// exception still counts as a real failure exactly as before.
    /// </remarks>
    [FrameworkDotnet.Attributes.FrameworkPlatformSpecific(FrameworkPlatformFamily.Framework16)]
    private bool TryReadExpansionBaySnapshot(IFrameworkEcConnection connection, ref string? snapshotError, out FrameworkExpansionBaySnapshot? snapshot)
    {
        try
        {
            snapshot = connection.GetExpansionBaySnapshot();
            _expansionBayUnavailableLogged = false;
            return true;
        }
        catch (FrameworkDotnet.Exceptions.EcResponseDetails.FrameworkUnavailableEcResponseException)
        {
            if (!_expansionBayUnavailableLogged)
            {
                _expansionBayUnavailableLogged = true;
                _logger.LogInformation("The expansion bay reports Unavailable — presenting it as an empty bay. This is normal for bay configurations that do not report (logged once per connection).");
            }

            // Door state is unreadable in this state; "closed" is the benign assumption (an open door is
            // a fault-adjacent signal nothing here can substantiate).
            snapshot = new FrameworkExpansionBaySnapshot(
                isPresent: false,
                isEnabled: false,
                hasFault: false,
                isDoorClosed: true,
                FrameworkExpansionBayBoard.NoModule,
                FrameworkExpansionBayVendor.Unknown,
                serialNumber: string.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to read the expansion bay snapshot.");
            snapshotError ??= exception.Message;
            snapshot = null;
            return false;
        }
    }

    private void PublishThermalTelemetry(FrameworkThermalSnapshot thermalSnapshot, DateTimeOffset observedAt)
    {
        _telemetryChannels.Edit(channelsUpdater =>
        _currentTelemetryValues.Edit(currentValuesUpdater =>
        _fanStates.Edit(fanStatesUpdater =>
        {
            _activeTelemetryChannelsUpdater = channelsUpdater;
            _activeCurrentValuesUpdater = currentValuesUpdater;
            _activeFanStatesUpdater = fanStatesUpdater;
            try
            {
                PublishThermalTelemetryCore(thermalSnapshot, observedAt);
            }
            finally
            {
                _activeFanStatesUpdater = null;
                _activeCurrentValuesUpdater = null;
                _activeTelemetryChannelsUpdater = null;
            }
        })));
    }

    private void PublishThermalTelemetryCore(FrameworkThermalSnapshot thermalSnapshot, DateTimeOffset observedAt)
    {
        var observedTemperatureChannels = new HashSet<TelemetryChannelId>();

        var temperatureCount = Math.Min((int)thermalSnapshot.SensorCount, thermalSnapshot.Temperatures.Count);

        for (var temperatureIndex = 0; temperatureIndex < temperatureCount; temperatureIndex++)
        {
            var temperatureSnapshot = thermalSnapshot.Temperatures[temperatureIndex];
            var channelId = new TelemetryChannelId(
                Area: TelemetryArea.Thermal,
                EntityKind: TelemetryEntityKind.TemperatureSensor,
                Index: temperatureIndex,
                Metric: TelemetryMetric.TemperatureCelsius);

            observedTemperatureChannels.Add(channelId);
            PublishNumericTelemetry(
                channelId: channelId,
                displayName: $"Temperature Sensor {temperatureIndex}",
                unitSymbol: "C",
                observedAt: observedAt,
                numericValue: temperatureSnapshot.Temperature.DegreesCelsius,
                temperatureState: temperatureSnapshot.State,
                sensorName: temperatureSnapshot.Name);
        }

        SetChannelsAvailability(
            area: TelemetryArea.Thermal,
            entityKind: TelemetryEntityKind.TemperatureSensor,
            observedChannels: observedTemperatureChannels,
            observedAt: observedAt);

        var observedFanChannels = new HashSet<TelemetryChannelId>();
        var observedFanIndices = new HashSet<int>();
        var fanIndex = 0;

        foreach (var fanSnapshot in thermalSnapshot.ReportedFans)
        {
            observedFanIndices.Add(fanIndex);

            // FrameworkDotnet now reports the physical fan location (Left/Right/APU/Front); fall back to the
            // index label for unknown/unnamed fans. Cache it so the capabilities stream uses the same name.
            var displayName = GetFanDisplayName(fanSnapshot.Name, fanIndex);
            _fanDisplayNames[fanIndex] = displayName;

            UpsertFanState(new FanStateSnapshot
            {
                FanIndex = fanIndex,
                DisplayName = displayName,

                // What it cools, not where it sits — the two differ on a Framework 16, where the right fan
                // is over the GPU. Resolved from the same EC slot role as the display name so they cannot
                // disagree.
                CoolingRole = FrameworkFanNameDisplay.ToRole(fanSnapshot.Name),
                FanState = fanSnapshot.FanState,
                ObservedAt = observedAt,
                IsAvailable = true,
            });

            var channelId = new TelemetryChannelId(
                Area: TelemetryArea.Thermal,
                EntityKind: TelemetryEntityKind.Fan,
                Index: fanIndex,
                Metric: TelemetryMetric.FanSpeedRpm);

            observedFanChannels.Add(channelId);
            PublishNumericTelemetry(
                channelId: channelId,
                displayName: displayName,
                unitSymbol: "RPM",
                observedAt: observedAt,
                numericValue: fanSnapshot.Speed.RevolutionsPerMinute,
                fanName: fanSnapshot.Name);

            fanIndex++;
        }

        var fanStatesItems = _activeFanStatesUpdater?.Items ?? (IEnumerable<FanStateSnapshot>)_fanStates.Items;
        var staleFanStates = fanStatesItems
            .Where(fanState => !observedFanIndices.Contains(fanState.FanIndex))
            .ToArray();

        foreach (var staleFanState in staleFanStates)
        {
            UpsertFanState(staleFanState with
            {
                ObservedAt = observedAt,
                IsAvailable = false,
            });
        }

        SetChannelsAvailability(
            area: TelemetryArea.Thermal,
            entityKind: TelemetryEntityKind.Fan,
            observedChannels: observedFanChannels,
            observedAt: observedAt);
    }

    private void PublishFanCapabilities(FrameworkFanCapabilitiesSnapshot fanCapabilitiesSnapshot, FrameworkPlatform? platform, FrameworkPlatformFamily? platformFamily, DateTimeOffset observedAt)
    {
        _fanCapabilities.Edit(updater =>
        {
            var observedFanIndices = new HashSet<int>();
            var coolingMetadata = FrameworkCoolingMetadataResolver.Resolve(platform, platformFamily);

            for (var fanIndex = 0; fanIndex < fanCapabilitiesSnapshot.FanCount; fanIndex++)
            {
                observedFanIndices.Add(fanIndex);
                updater.AddOrUpdate(new FanCapabilityState
                {
                    FanIndex = fanIndex,
                    DisplayName = _fanDisplayNames.GetValueOrDefault(fanIndex, $"Fan {fanIndex}"),
                    Features = fanCapabilitiesSnapshot.Features,
                    SupportsFanControl = fanCapabilitiesSnapshot.Features.HasFlag(FrameworkFanFeaturesState.FanControl),
                    SupportsThermalReporting = fanCapabilitiesSnapshot.Features.HasFlag(FrameworkFanFeaturesState.ThermalReporting),
                    MaximumSpeedRpm = coolingMetadata.MaximumSpeedRpm,
                    CoolingDetails = coolingMetadata.CoolingDetails,
                    ObservedAt = observedAt,
                    IsAvailable = true,
                });
            }

            var staleCapabilities = updater.Items
                .Where(capability => !observedFanIndices.Contains(capability.FanIndex))
                .ToArray();

            foreach (var staleCapability in staleCapabilities)
            {
                updater.AddOrUpdate(staleCapability with
                {
                    ObservedAt = observedAt,
                    IsAvailable = false,
                });
            }
        });
    }

    // Projects the USB-C expansion-card slots' Power Delivery state into the decoupled snapshot the gRPC
    // boundary and UI consume, dropping the rest of the (heavier) module inventory.
    /// <summary>UI slot index for the Framework 16 graphics-module USB-C port — EC PD index 4, after the four
    /// mainboard PD ports (0–3).</summary>
    private const int GraphicsModulePortIndex = 4;

    private static PowerDeliverySnapshot BuildPowerDeliverySnapshot(
        FrameworkModuleInventorySnapshot inventory,
        FrameworkExpansionBaySnapshot? expansionBay)
    {
        var ports = new List<PowerDeliveryPortSnapshot>();
        foreach (var slot in inventory.ReportedUsbCSlots)
        {
            var pd = slot.PowerDelivery;
            var capability = slot.Capability;
            ports.Add(new PowerDeliveryPortSnapshot
            {
                SlotIndex = slot.SlotIndex,
                IsPresent = slot.IsPresent,
                IsActivePort = pd.IsActivePort,
                HasPowerDeliveryContract = pd.HasPowerDeliveryContract,
                CState = pd.CState,
                PowerRole = pd.PowerRole,
                DataRole = pd.DataRole,
                CcPolarity = pd.CcPolarity,
                VoltageVolts = pd.Voltage.Volts,
                CurrentAmperes = pd.Current.Amperes,
                IsVconnActive = pd.IsVconnActive,
                IsEprActive = pd.IsEprActive,
                IsEprSupported = pd.IsEprSupported,
                AltModeFlags = pd.AltModeFlags,
                CardType = slot.CardType.ToString(),
                DataLane = capability.DataLane,
                DisplayPortCapability = capability.DisplayPort,
                SupportsCharging = capability.SupportsPowerDelivery,
                MaxChargeWatts = (int)System.Math.Round(capability.MaxChargePower.Watts),
                UsbAHighPower = capability.UsbAHighPowerDraw,
                CapabilityDocumented = capability.IsDocumented,
                PortSource = "Mainboard",
                PortPosition = capability.PositionName,
                PortIsLeft = capability.IsLeftSide,
            });
        }

        // The Framework 16 graphics module adds a 5th PD port (EC index 4) with its own USB-C port. Append it
        // after the four mainboard ports, sourced from the GPU.
        if (expansionBay is { HasUsbCPort: true, UsbCPort: { } bayPd })
        {
            var bayCapability = expansionBay.UsbCCapability;
            ports.Add(new PowerDeliveryPortSnapshot
            {
                SlotIndex = GraphicsModulePortIndex,
                PortSource = "GraphicsModule",
                IsPresent = true,
                IsActivePort = bayPd.IsActivePort,
                HasPowerDeliveryContract = bayPd.HasPowerDeliveryContract,
                CState = bayPd.CState,
                PowerRole = bayPd.PowerRole,
                DataRole = bayPd.DataRole,
                CcPolarity = bayPd.CcPolarity,
                VoltageVolts = bayPd.Voltage.Volts,
                CurrentAmperes = bayPd.Current.Amperes,
                IsVconnActive = bayPd.IsVconnActive,
                IsEprActive = bayPd.IsEprActive,
                IsEprSupported = bayPd.IsEprSupported,
                AltModeFlags = bayPd.AltModeFlags,
                CardType = "Unknown",
                DataLane = bayCapability?.DataLane ?? FrameworkUsbCDataLane.Unknown,
                DisplayPortCapability = bayCapability?.DisplayPort ?? FrameworkDisplayPortCapability.None,
                SupportsCharging = bayCapability?.SupportsPowerDelivery ?? false,
                MaxChargeWatts = bayCapability is null ? 0 : (int)System.Math.Round(bayCapability.MaxChargePower.Watts),
                UsbAHighPower = bayCapability?.UsbAHighPowerDraw ?? false,
                CapabilityDocumented = bayCapability?.IsDocumented ?? false,
                PortPosition = bayCapability?.PositionName ?? "Graphics module",
                PortIsLeft = bayCapability?.IsLeftSide ?? false,
            });
        }

        return new PowerDeliverySnapshot { Ports = ports };
    }

    private static ModuleInventorySnapshot BuildModuleInventorySnapshot(
        FrameworkModuleInventorySnapshot inventory,
        FrameworkExpansionBaySnapshot? expansionBay)
    {
        static ModuleDescriptorSnapshot Map(FrameworkModuleDescriptorSnapshot descriptor) => new()
        {
            Identity = descriptor.Identity,
            Bus = descriptor.Bus,
            SlotKind = descriptor.SlotKind,
            Confidence = descriptor.Confidence,
            IsPresent = descriptor.IsPresent,
            SlotIndex = descriptor.SlotIndex,
            Flags = descriptor.Flags,
            VendorId = descriptor.VendorId,
            ProductId = descriptor.ProductId,
            BoardId = descriptor.BoardId,
            Position = descriptor.Position,
            CardType = FrameworkExpansionCardType.Unknown,
            CardConfidence = FrameworkModuleConfidence.Unknown,
        };

        List<ModuleDescriptorSnapshot> usbCSlots = [];
        foreach (var slot in inventory.ReportedUsbCSlots)
        {
            usbCSlots.Add(new ModuleDescriptorSnapshot
            {
                Identity = slot.Identity,
                Bus = slot.Bus,
                SlotKind = slot.SlotKind,
                Confidence = slot.Confidence,
                IsPresent = slot.IsPresent,
                SlotIndex = slot.SlotIndex,
                Flags = slot.Flags,
                VendorId = slot.VendorId,
                ProductId = slot.ProductId,
                BoardId = slot.BoardId,
                Position = FrameworkInputModulePosition.Unknown,
                CardType = slot.CardType,
                CardConfidence = slot.CardConfidence,
            });
        }

        List<ModuleDescriptorSnapshot> inputDeck = [.. inventory.ReportedInputTopRowModules.Select(Map)];
        if (inventory.InputTouchpad.IsPresent)
        {
            inputDeck.Add(Map(inventory.InputTouchpad));
        }

        List<ModuleDescriptorSnapshot> internals = [];
        foreach (var fixedModule in new[] { inventory.InternalKeyboard, inventory.InternalTouchpad, inventory.FingerprintReader, inventory.Touchscreen, inventory.Webcam })
        {
            if (fixedModule.IsPresent)
            {
                internals.Add(Map(fixedModule));
            }
        }

        // The bay snapshot (FW16-only read) refines the generic inventory classification (e.g. "ExpansionBay"
        // → "ExpansionBayAmdGpu"); keep the inventory descriptor for the IDs/flags and overlay the identity.
        ModuleDescriptorSnapshot? bayModule = null;
        if (inventory.ExpansionBay.IsPresent)
        {
            bayModule = Map(inventory.ExpansionBay);
            if (expansionBay is { Identity: not FrameworkModuleIdentity.None } refinedBay)
            {
                bayModule = bayModule with { Identity = refinedBay.Identity };
            }
        }

        return new ModuleInventorySnapshot
        {
            UsbCSlots = usbCSlots,
            InputDeckModules = inputDeck,
            InternalModules = internals,
            DetachedModules = [.. inventory.ReportedDetachedModules.Select(Map)],
            ExpansionBayModule = bayModule,
            ExpansionBayBoard = expansionBay?.Board ?? FrameworkExpansionBayBoard.Unknown,
            ExpansionBayVendor = expansionBay?.Vendor ?? FrameworkExpansionBayVendor.Unknown,
            ExpansionBaySerialNumber = expansionBay?.SerialNumber ?? string.Empty,
        };
    }


    private void PublishPowerTelemetry(FrameworkPowerSnapshot powerSnapshot, DateTimeOffset observedAt)
    {
        _telemetryChannels.Edit(channelsUpdater =>
        _currentTelemetryValues.Edit(currentValuesUpdater =>
        {
            _activeTelemetryChannelsUpdater = channelsUpdater;
            _activeCurrentValuesUpdater = currentValuesUpdater;
            try
            {
                PublishPowerTelemetryCore(powerSnapshot, observedAt);
            }
            finally
            {
                _activeCurrentValuesUpdater = null;
                _activeTelemetryChannelsUpdater = null;
            }
        }));
    }

    private void PublishPowerTelemetryCore(FrameworkPowerSnapshot powerSnapshot, DateTimeOffset observedAt)
    {
        // Refresh the feed-forward load figure here, where charger draw and battery draw are both in hand.
        if (_latestPowerDeliverySnapshot is { } powerDelivery)
        {
            CacheSystemPower(powerSnapshot, powerDelivery);
        }

        var observedBatteryChannels = new HashSet<TelemetryChannelId>();
        var batteryIndex = 0;

        foreach (var batterySnapshot in powerSnapshot.ReportedBatteries)
        {
            PublishBatteryMetric(
                metric: TelemetryMetric.BatteryChargePercent,
                metricName: "Charge",
                unitSymbol: "%",
                batteryIndex: batteryIndex,
                observedAt: observedAt,
                numericValue: batterySnapshot.ChargeLevel.Percent,
                batterySnapshot: batterySnapshot,
                powerSourceState: powerSnapshot.PowerSourceState,
                observedChannels: observedBatteryChannels);

            PublishBatteryMetric(
                metric: TelemetryMetric.BatteryPresentRateAmperes,
                metricName: "Present Rate",
                unitSymbol: "A",
                batteryIndex: batteryIndex,
                observedAt: observedAt,
                numericValue: batterySnapshot.BatteryState == FrameworkBatteryState.Discharging ? (-batterySnapshot.PresentRate.Amperes) : (batterySnapshot.PresentRate.Amperes),
                batterySnapshot: batterySnapshot,
                powerSourceState: powerSnapshot.PowerSourceState,
                observedChannels: observedBatteryChannels);

            PublishBatteryMetric(
                metric: TelemetryMetric.BatteryPresentVoltageVolts,
                metricName: "Present Voltage",
                unitSymbol: "V",
                batteryIndex: batteryIndex,
                observedAt: observedAt,
                numericValue: batterySnapshot.PresentVoltage.Volts,
                batterySnapshot: batterySnapshot,
                powerSourceState: powerSnapshot.PowerSourceState,
                observedChannels: observedBatteryChannels);

            batteryIndex++;
        }

        SetChannelsAvailability(
            area: TelemetryArea.Power,
            entityKind: TelemetryEntityKind.Battery,
            observedChannels: observedBatteryChannels,
            observedAt: observedAt);
    }

    private void PublishBatteryMetric(
        TelemetryMetric metric,
        string metricName,
        string unitSymbol,
        int batteryIndex,
        DateTimeOffset observedAt,
        double numericValue,
        FrameworkBatterySnapshot batterySnapshot,
        FrameworkPowerSourceState powerSourceState,
        ISet<TelemetryChannelId> observedChannels)
    {
        var channelId = new TelemetryChannelId(
            Area: TelemetryArea.Power,
            EntityKind: TelemetryEntityKind.Battery,
            Index: batteryIndex,
            Metric: metric);

        observedChannels.Add(channelId);
        PublishNumericTelemetry(
            channelId: channelId,
            displayName: $"Battery {batteryIndex} {metricName}",
            unitSymbol: unitSymbol,
            observedAt: observedAt,
                numericValue: numericValue,
                powerSourceState: powerSourceState,
                batteryState: batterySnapshot.BatteryState,
                batteryManufacturer: batterySnapshot.Manufacturer,
                batteryModelNumber: batterySnapshot.ModelNumber,
                batterySerialNumber: batterySnapshot.SerialNumber,
                batteryType: batterySnapshot.BatteryType,
                batteryRemainingCapacityAmpereHours: batterySnapshot.RemainingCapacity.AmpereHours,
                batteryDesignCapacityAmpereHours: batterySnapshot.DesignCapacity.AmpereHours,
                batteryLastFullChargeCapacityAmpereHours: batterySnapshot.LastFullChargeCapacity.AmpereHours,
                batteryDesignVoltageVolts: batterySnapshot.DesignVoltage.Volts,
                batteryCycleCount: batterySnapshot.CycleCount);
    }

    private void PublishNumericTelemetry(
        TelemetryChannelId channelId,
        string displayName,
        string unitSymbol,
        DateTimeOffset observedAt,
        double numericValue,
        FrameworkTemperatureState? temperatureState = null,
        FrameworkSensorName? sensorName = null,
        FrameworkFanName? fanName = null,
        FrameworkPowerSourceState? powerSourceState = null,
        FrameworkBatteryState? batteryState = null,
        string? batteryManufacturer = null,
        string? batteryModelNumber = null,
        string? batterySerialNumber = null,
        string? batteryType = null,
        double? batteryRemainingCapacityAmpereHours = null,
        double? batteryDesignCapacityAmpereHours = null,
        double? batteryLastFullChargeCapacityAmpereHours = null,
        double? batteryDesignVoltageVolts = null,
        uint? batteryCycleCount = null,
        ComputeDeviceUtilization? computeDevice = null)
    {
        _lastTelemetryObservedAt = observedAt;
        UpsertChannel(channelId, displayName, unitSymbol, observedAt, isAvailable: true);
        var currentValue = new CurrentTelemetryValue
        {
            ChannelId = channelId,
            DisplayName = displayName,
            UnitSymbol = unitSymbol,
            ObservedAt = observedAt,
            NumericValue = numericValue,
            TemperatureState = temperatureState,
            SensorName = sensorName,
            FanName = fanName,
            PowerSourceState = powerSourceState,
            BatteryState = batteryState,
            BatteryManufacturer = batteryManufacturer,
            BatteryModelNumber = batteryModelNumber,
            BatterySerialNumber = batterySerialNumber,
            BatteryType = batteryType,
            BatteryRemainingCapacityAmpereHours = batteryRemainingCapacityAmpereHours,
            BatteryDesignCapacityAmpereHours = batteryDesignCapacityAmpereHours,
            BatteryLastFullChargeCapacityAmpereHours = batteryLastFullChargeCapacityAmpereHours,
            BatteryDesignVoltageVolts = batteryDesignVoltageVolts,
            BatteryCycleCount = batteryCycleCount,

            // Taken from the device record rather than as five more parameters: they are measured together in
            // one source call, and passing them as a unit is what keeps them from being split across ticks.
            ComputePowerWatts = computeDevice?.PowerWatts,
            ComputeTemperatureCelsius = computeDevice?.TemperatureCelsius,
            ComputeCoreClockMegahertz = computeDevice?.CoreClockMegahertz,
            ComputeMaxCoreClockMegahertz = computeDevice?.MaxCoreClockMegahertz,
            ComputeVramUsedBytes = computeDevice?.VramUsedBytes,
            ComputeVramTotalBytes = computeDevice?.VramTotalBytes,
            ComputeThrottleReasons = computeDevice?.ThrottleReasons,

            IsAvailable = true,
        };

        if (_activeCurrentValuesUpdater is { } currentValuesUpdater)
        {
            currentValuesUpdater.AddOrUpdate(currentValue);
        }
        else
        {
            _currentTelemetryValues.AddOrUpdate(currentValue);
        }

        _telemetryPoints.AddOrUpdate(new TelemetryPoint(
            SampleId: Interlocked.Increment(ref _nextTelemetryPointId),
            ChannelId: channelId,
            ObservedAt: observedAt,
            NumericValue: numericValue));
    }

    private void UpsertFanState(FanStateSnapshot fanState)
    {
        if (_activeFanStatesUpdater is { } updater)
        {
            updater.AddOrUpdate(fanState);
        }
        else
        {
            _fanStates.AddOrUpdate(fanState);
        }
    }

    private void UpsertChannel(
        TelemetryChannelId channelId,
        string displayName,
        string unitSymbol,
        DateTimeOffset observedAt,
        bool isAvailable)
    {
        var channelsItems = _activeTelemetryChannelsUpdater?.Items ?? (IEnumerable<TelemetryChannel>)_telemetryChannels.Items;
        var existingChannel = channelsItems.FirstOrDefault(channel => channel.Id == channelId);

        if (existingChannel is null)
        {
            var added = new TelemetryChannel
            {
                Id = channelId,
                DisplayName = displayName,
                UnitSymbol = unitSymbol,
                FirstObservedAt = observedAt,
                LastObservedAt = observedAt,
                IsAvailable = isAvailable,
            };
            if (_activeTelemetryChannelsUpdater is { } addUpdater)
            {
                addUpdater.AddOrUpdate(added);
            }
            else
            {
                _telemetryChannels.AddOrUpdate(added);
            }
            return;
        }

        var updated = existingChannel with
        {
            DisplayName = displayName,
            UnitSymbol = unitSymbol,
            LastObservedAt = observedAt,
            IsAvailable = isAvailable,
        };
        if (_activeTelemetryChannelsUpdater is { } updateUpdater)
        {
            updateUpdater.AddOrUpdate(updated);
        }
        else
        {
            _telemetryChannels.AddOrUpdate(updated);
        }
    }

    private void SetChannelsAvailability(
        TelemetryArea area,
        TelemetryEntityKind entityKind,
        IReadOnlySet<TelemetryChannelId> observedChannels,
        DateTimeOffset observedAt)
    {
        var channelsItems = _activeTelemetryChannelsUpdater?.Items ?? (IEnumerable<TelemetryChannel>)_telemetryChannels.Items;
        var staleChannels = channelsItems
            .Where(channel => channel.Id.Area == area && channel.Id.EntityKind == entityKind && !observedChannels.Contains(channel.Id))
            .ToArray();

        foreach (var staleChannel in staleChannels)
        {
            if (staleChannel.IsAvailable)
            {
                var updatedChannel = staleChannel with { IsAvailable = false };
                if (_activeTelemetryChannelsUpdater is { } channelsUpdater)
                {
                    channelsUpdater.AddOrUpdate(updatedChannel);
                }
                else
                {
                    _telemetryChannels.AddOrUpdate(updatedChannel);
                }
            }

            var unavailableValue = new CurrentTelemetryValue
            {
                ChannelId = staleChannel.Id,
                DisplayName = staleChannel.DisplayName,
                UnitSymbol = staleChannel.UnitSymbol,
                ObservedAt = observedAt,
                NumericValue = null,
                TemperatureState = null,
                PowerSourceState = null,
                BatteryState = null,
                BatteryManufacturer = null,
                BatteryModelNumber = null,
                BatterySerialNumber = null,
                BatteryType = null,
                BatteryRemainingCapacityAmpereHours = null,
                BatteryDesignCapacityAmpereHours = null,
                BatteryLastFullChargeCapacityAmpereHours = null,
                BatteryDesignVoltageVolts = null,
                BatteryCycleCount = null,
                IsAvailable = false,
            };

            if (_activeCurrentValuesUpdater is { } currentValuesUpdater)
            {
                currentValuesUpdater.AddOrUpdate(unavailableValue);
            }
            else
            {
                _currentTelemetryValues.AddOrUpdate(unavailableValue);
            }
        }
    }

    private void MarkAllTelemetryUnavailable(DateTimeOffset observedAt)
    {
        _telemetryChannels.Edit(channelsUpdater =>
        _currentTelemetryValues.Edit(currentValuesUpdater =>
        {
            _activeTelemetryChannelsUpdater = channelsUpdater;
            _activeCurrentValuesUpdater = currentValuesUpdater;
            try
            {
                foreach (var channel in channelsUpdater.Items.ToArray())
                {
                    if (channel.IsAvailable)
                    {
                        channelsUpdater.AddOrUpdate(channel with { IsAvailable = false });
                    }

                    currentValuesUpdater.AddOrUpdate(new CurrentTelemetryValue
                    {
                        ChannelId = channel.Id,
                        DisplayName = channel.DisplayName,
                        UnitSymbol = channel.UnitSymbol,
                        ObservedAt = observedAt,
                        NumericValue = null,
                        TemperatureState = null,
                        PowerSourceState = null,
                        BatteryState = null,
                        BatteryManufacturer = null,
                        BatteryModelNumber = null,
                        BatterySerialNumber = null,
                        BatteryType = null,
                        BatteryRemainingCapacityAmpereHours = null,
                        BatteryDesignCapacityAmpereHours = null,
                        BatteryLastFullChargeCapacityAmpereHours = null,
                        BatteryDesignVoltageVolts = null,
                        BatteryCycleCount = null,
                        IsAvailable = false,
                    });
                }
            }
            finally
            {
                _activeCurrentValuesUpdater = null;
                _activeTelemetryChannelsUpdater = null;
            }
        }));

        MarkAllFanCapabilitiesUnavailable(observedAt);
        MarkAllFanStatesUnavailable(observedAt);
    }

    private void MarkAllFanCapabilitiesUnavailable(DateTimeOffset observedAt)
    {
        _fanCapabilities.Edit(updater =>
        {
            foreach (var fanCapability in updater.Items.ToArray())
            {
                if (!fanCapability.IsAvailable)
                {
                    continue;
                }

                updater.AddOrUpdate(fanCapability with
                {
                    ObservedAt = observedAt,
                    IsAvailable = false,
                });
            }
        });
    }

    private void MarkAllFanStatesUnavailable(DateTimeOffset observedAt)
    {
        _fanStates.Edit(updater =>
        {
            foreach (var fanState in updater.Items.ToArray())
            {
                if (!fanState.IsAvailable)
                {
                    continue;
                }

                updater.AddOrUpdate(fanState with
                {
                    ObservedAt = observedAt,
                    IsAvailable = false,
                });
            }
        });
    }

    public void RestoreAutomaticFanControl()
    {
        var fansToRestore = _fanControlSafetyTracker.BeginRestoreBatch();
        if (fansToRestore.Length == 0)
        {
            return;
        }

        if (_connection is null)
        {
            _logger.LogWarning("Skipping automatic fan control restore because no EC connection is available for {FanCount} overridden fan(s).", fansToRestore.Length);

            foreach (var fanIndex in fansToRestore)
            {
                _fanControlSafetyTracker.CompleteRestore(fanIndex, restored: false, errorMessage: "The EC connection was unavailable while automatic fan control was being restored.");
            }

            return;
        }

        // Handing the fans back is the safety net that runs when polling stops, so record that it ran at
        // all — a silent success and a never-called restore look identical after the fact otherwise.
        LogAutomaticFanControlRestoreBatch(fansToRestore.Length);

        foreach (var fanIndex in fansToRestore)
        {
            try
            {
                _connection.RestoreAutoFanControl(fanIndex);
                _fanControlSafetyTracker.CompleteRestore(fanIndex, restored: true);
                LogAutoFanControlRestored(fanIndex);
            }
            catch (Exception exception)
            {
                _fanControlSafetyTracker.CompleteRestore(fanIndex, restored: false, errorMessage: exception.Message);
                _logger.LogWarning(exception, "Unable to restore automatic fan control for fan {FanIndex}.", fanIndex);
            }
        }
    }

    private void TryReadStatusValue<T>(Func<T> readValue, string operation, ref string? lastError, Action<T> assignValue)
    {
        try
        {
            assignValue(readValue());
        }
        catch (Exception exception)
        {
            CaptureStatusReadFailure(exception, operation, ref lastError);
        }
    }

    private void CaptureStatusReadFailure(Exception exception, string operation, ref string? lastError)
    {
        _logger.LogWarning(exception, "Unable to {Operation}.", operation);
        lastError ??= exception.Message;
    }

    private async Task RunPollingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Timed around the WORK, so the sleep below can give back only the unused remainder of the
                // interval. Sleeping the whole interval regardless would make the real period interval+work.
                var tickStartedAt = Stopwatch.GetTimestamp();

                try
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "The Framework polling loop failed.");
                }

                var pollingInterval = GetPollingIntervalOrDefault();
                if (pollingInterval is null)
                {
                    break;
                }

                var elapsed = Stopwatch.GetElapsedTime(tickStartedAt);
                ReportTierOverrunIfNeeded("primary", pollingInterval.Value, elapsed, ref _primaryOverrunLogged);
                await Task.Delay(PollingSchedule.ComputeDelay(pollingInterval.Value, elapsed), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            RestoreAutomaticFanControl();
            DisposeConnection();

            lock (_syncLock)
            {
                _isPolling = false;
                _pollingTask = null;
                _pollingCancellation = null;
            }
        }
    }


    /// <summary>
    /// Warns once when a tier cannot finish its work inside its own interval.
    /// </summary>
    /// <remarks>
    /// A tier in this state runs flat out with no idle time, which is worth saying plainly: the symptom
    /// otherwise is just "the interval setting does not seem to do anything", because the loop is already
    /// going as fast as the work allows and shortening the interval changes nothing.
    /// </remarks>
    private void ReportTierOverrunIfNeeded(string tierName, TimeSpan interval, TimeSpan elapsed, ref bool alreadyLogged)
    {
        if (elapsed <= interval || alreadyLogged)
        {
            return;
        }

        alreadyLogged = true;
        _logger.LogWarning(
            "The {TierName} polling tier took {ElapsedMs:F0} ms, longer than its {IntervalMs:F0} ms interval, so it is running without idle time. Ticks are not skipped or queued; the next one starts immediately.",
            tierName,
            elapsed.TotalMilliseconds,
            interval.TotalMilliseconds);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static TimeSpan ValidateHistoryWindow(TimeSpan historyWindow)
    {
        if (historyWindow <= TimeSpan.Zero || historyWindow > TelemetryHistoryLimits.MaximumHistoryWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(historyWindow), $"History window must be between {TimeSpan.Zero} and {TelemetryHistoryLimits.MaximumHistoryWindow}.");
        }

        return historyWindow;
    }

    // WMI per-core names are "socket,core" (e.g. "0,11") or a bare index; unparsable names sort last.
    private static int ParseCpuCoreOrdinal(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return int.MaxValue;
        }

        var commaIndex = name.LastIndexOf(',');
        var candidate = commaIndex >= 0 ? name[(commaIndex + 1)..] : name;
        return int.TryParse(candidate.Trim(), out var ordinal) ? ordinal : int.MaxValue;
    }

    private static string GetMonitorDisplayName(HardwareMonitor monitor)
        => FirstNonEmpty(monitor.UserFriendlyName, monitor.Name, monitor.Caption, monitor.Description) ?? "Unknown monitor";

    private static string GetVideoControllerDisplayName(HardwareVideoController videoController)
        => FirstNonEmpty(videoController.Name, videoController.Caption, videoController.Description, videoController.VideoProcessor) ?? "Unknown adapter";

    // Maps the FrameworkDotnet fan-location enum to the friendly labels the redesigned UI shows
    // (e.g. "Left fan"). Unknown/None or any unmapped value falls back to the positional label.
    // FD0001 (platform-specific enum members) is intentionally suppressed: we translate whatever name the
    // device itself reported, so only the cases valid for the running platform are ever hit; the rest are inert.
#pragma warning disable FD0001
    private static string GetFanDisplayName(FrameworkFanName name, int fanIndex) => name switch
    {
        FrameworkFanName.LeftFan => "Left fan",
        FrameworkFanName.RightFan => "Right fan",
        FrameworkFanName.ApuFan => "APU fan",
        FrameworkFanName.FrontFan => "Front fan",
        _ => $"Fan {fanIndex}",
    };
#pragma warning restore FD0001

    private static bool MatchesMonitorIdentity(HardwareMonitor left, HardwareMonitor right)
    {
        if (!string.IsNullOrWhiteSpace(left.ProductCodeID)
            && !string.IsNullOrWhiteSpace(left.SerialNumberID)
            && string.Equals(left.ProductCodeID, right.ProductCodeID, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.SerialNumberID, right.SerialNumberID, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(GetMonitorDisplayName(left), GetMonitorDisplayName(right), StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                FirstNonEmpty(left.ManufacturerName, left.MonitorManufacturer),
                FirstNonEmpty(right.ManufacturerName, right.MonitorManufacturer),
                StringComparison.OrdinalIgnoreCase)
            && left.CurrentHorizontalResolution == right.CurrentHorizontalResolution
            && left.CurrentVerticalResolution == right.CurrentVerticalResolution;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static TelemetryChannelId CreateBatteryChannelId(int batteryIndex, TelemetryMetric metric)
        => new(TelemetryArea.Power, TelemetryEntityKind.Battery, batteryIndex, metric);

    private TimeSpan? GetPollingIntervalOrDefault()
    {
        lock (_syncLock)
        {
            return _isPolling ? _pollingInterval : null;
        }
    }

    private void StopPollingIfRunning()
    {
        if (!_isPolling && (_pollingTask is null || _pollingTask.IsCompleted))
        {
            return;
        }

        _ = StopPolling();
    }

    // Trace records for the EC boundary. Every hardware write is logged twice — once with what we asked
    // for and once with what the EC reported back — because those two values genuinely differ (duty is
    // rounded to a whole percent here, and the EC is free to clamp anything it is handed).
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EC write: fan {FanIndex} target speed {TargetSpeedRpm} RPM.")]
    private partial void LogFanRpmWriteRequested(int fanIndex, int targetSpeedRpm);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EC write applied: fan {FanIndex} asked for {TargetSpeedRpm} RPM, EC reports {AppliedSpeedRpm:F0} RPM.")]
    private partial void LogFanRpmWriteApplied(int fanIndex, int targetSpeedRpm, double appliedSpeedRpm);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EC write: fan {FanIndex} duty {RequestedDutyPercent:F2}% rounded to {WholeDutyPercent:F0}% for the duty register.")]
    private partial void LogFanDutyWriteRequested(int fanIndex, double requestedDutyPercent, double wholeDutyPercent);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EC write applied: fan {FanIndex} wrote {WholeDutyPercent:F0}%, EC reports {AppliedDutyPercent:F0}%.")]
    private partial void LogFanDutyWriteApplied(int fanIndex, double wholeDutyPercent, double appliedDutyPercent);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EC write: handing fan {FanIndex} back to automatic control.")]
    private partial void LogAutoFanControlRestoreRequested(int fanIndex);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EC write applied: fan {FanIndex} is back under automatic control.")]
    private partial void LogAutoFanControlRestored(int fanIndex);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Restoring automatic fan control for {FanCount} fan(s) that this process had overridden.")]
    private partial void LogAutomaticFanControlRestoreBatch(int fanCount);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Read the {SnapshotName} snapshot in {ElapsedMilliseconds:F1} ms.")]
    private partial void LogSnapshotRead(string snapshotName, double elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "EC poll finished: {SuccessfulReads} snapshot(s) read in {ElapsedMilliseconds:F1} ms. Error: {SnapshotError}.")]
    private partial void LogEcPollCompleted(int successfulReads, double elapsedMilliseconds, string snapshotError);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "HardwareInfo poll finished in {ElapsedMilliseconds:F1} ms. IsAvailable={IsAvailable}. Error: {LastError}.")]
    private partial void LogHardwareInfoPollCompleted(double elapsedMilliseconds, bool isAvailable, string lastError);
}
