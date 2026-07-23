using DynamicData;

using System.Collections.Immutable;
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
using UnitsNet;

namespace SubZeroFramework.Services;

public sealed class FrameworkDataProvider : IFrameworkDataProvider, IDisposable
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
        IHardwareInfoLogNoiseBuffer? hardwareInfoNoiseBuffer = null)
    {
        _frameworkSystem = frameworkSystem;
        _hardwareInfo = hardwareInfo;
        _fanControlSafetyTracker = fanControlSafetyTracker;
        _logger = logger;
        _hardwareInfoNoiseBuffer = hardwareInfoNoiseBuffer ?? NullHardwareInfoLogNoiseBuffer.Instance;
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

        lock (_syncLock)
        {
            if (_isPolling)
            {
                return false;
            }

            _pollingInterval = pollingInterval;
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

        lock (_syncLock)
        {
            if (_isHardwareInfoPolling)
            {
                return false;
            }

            _hardwareInfoPollingInterval = pollingInterval;
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

        try
        {
            using (var cpuCapture = _hardwareInfoNoiseBuffer.BeginCapture())
            {
                _hardwareInfo.RefreshCPUList(true, 500, true);
                cpuCapture.SetDataPresent(_hardwareInfo.CpuList?.Count > 0);
            }

            _hardwareInfo.RefreshMemoryList();
            _hardwareInfo.RefreshDriveList();
            _hardwareInfo.RefreshMotherboardList();
            _hardwareInfo.RefreshBIOSList();
            _hardwareInfo.RefreshComputerSystemList();
            _hardwareInfo.RefreshOperatingSystem();
            _hardwareInfo.RefreshNetworkAdapterList(
                includeBytesPerSec: false,
                includeNetworkAdapterConfiguration: true,
                millisecondsDelayBetweenTwoMeasurements: 0);
            // Display/GPU enumeration is skipped entirely on Linux. Hardware.Info implements BOTH the
            // video-controller list ("xrandr -q") and the monitor list ("xrandr --props") by shelling out
            // to xrandr, and neither can work from here under ANY desktop stack:
            //
            //   * This process is a root systemd unit whose environment carries no DISPLAY,
            //     WAYLAND_DISPLAY or XAUTHORITY (see SubZeroFramework.Service/subzeroframework.service),
            //     so there is no display server to talk to. Verified by running xrandr with those
            //     variables stripped, WITH the package installed: it prints "Can't open display".
            //   * On a Wayland session (the reporting user runs Hyprland) xrandr can at best reach
            //     XWayland, which warns it is doing so and reports a synthetic view rather than the real
            //     outputs — so shipping the package would not have fixed it either.
            //
            // Cost of asking anyway: two failed process spawns per poll returning nothing — 56 failures
            // in 22 s measured on Arch, ~96 over 3 min observed on a Framework 13.
            //
            // Doing this properly on Linux means reading EDID from /sys/class/drm, which needs no display
            // server and works headless, on X11 and on Wayland alike; or querying from the client, which
            // does have a display connection. Tracked as a post-0.1.0 item.
            if (!OperatingSystem.IsLinux())
            {
                _hardwareInfo.RefreshVideoControllerList(refreshMonitorList: true);
            }

            _hardwareInfo.RefreshMemoryStatus();
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
                cpus = _hardwareInfo.CpuList
                    .Select(cpu =>
                    {
                        // WMI enumerates cores in string order ("0", "1", "10", "11", … "2") — sort numerically.
                        var mappedCpuCores = cpu.CpuCoreList
                            .Where(core => !string.IsNullOrWhiteSpace(core.Name))
                            .Select(core => new HardwareInfoCpuCore(
                                Name: core.Name,
                                PercentProcessorTime: Math.Clamp((double)core.PercentProcessorTime, 0d, 100d)))
                            .OrderBy(core => ParseCpuCoreOrdinal(core.Name))
                            .ThenBy(core => core.Name, StringComparer.Ordinal)
                            .ToImmutableArray();

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
                            PercentProcessorTime: mappedCpuCores.Length > 0 || cpu.PercentProcessorTime > 0
                                ? Math.Clamp((double)cpu.PercentProcessorTime, 0d, 100d)
                                : null,
                            CpuCores: mappedCpuCores);
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
            if (_hardwareInfo.MemoryList.Count > 0)
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
            if (_hardwareInfo.DriveList.Count > 0)
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
                try
                {
                    var snapshot = ReadHardwareInfoSnapshot();
                    PublishHardwareInfoSnapshot(snapshot, snapshot.ObservedAt);
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

                await Task.Delay(pollingInterval.Value, cancellationToken).ConfigureAwait(false);
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
            _logger.LogDebug("Publishing power snapshot for {BatteryCount} battery or batteries at {ObservedAt}.", powerSnapshot!.ReportedBatteries.Count(), observedAt);
            _powerSnapshots.Publish(powerSnapshot!, observedAt);
            PublishPowerTelemetry(powerSnapshot!, observedAt);
            successfulReads += 1;
        }

        if (observedAt - _lastModuleInventoryReadAt >= ModuleInventoryReadInterval
            && TryReadSnapshot(connection.GetModuleInventorySnapshot, "module inventory", ref snapshotError, out var moduleInventory))
        {
            _lastModuleInventoryReadAt = observedAt;
            FrameworkExpansionBaySnapshot? expansionBay = null;
            if (connectedStatus.PlatformFamily == FrameworkPlatformFamily.Framework16)
            {
                TryReadExpansionBaySnapshot(connection, ref snapshotError, out expansionBay);
            }

            _powerDeliverySnapshots.Publish(BuildPowerDeliverySnapshot(moduleInventory!, expansionBay), observedAt);
            _moduleInventorySnapshots.Publish(BuildModuleInventorySnapshot(moduleInventory!, expansionBay), observedAt);
            successfulReads += 1;
        }

        if (TryReadSnapshot(connection.GetThermalSnapshot, "thermal", ref snapshotError, out var thermalSnapshot))
        {
            _logger.LogDebug("Publishing thermal snapshot for {SensorCount} sensor(s) and {FanCount} reported fan(s) at {ObservedAt}.", thermalSnapshot!.SensorCount, thermalSnapshot.ReportedFans.Count(), observedAt);
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
        FrameworkSetFanRpmResponse response = connection.SetFanRpm(fanIndex, RotationalSpeed.FromRevolutionsPerMinute(targetSpeedRpm));
        _fanControlSafetyTracker.MarkOverrideActive(response.FanIndex);

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
        FrameworkSetFanDutyResponse response = connection.SetFanDuty(fanIndex, Ratio.FromPercent(wholeDutyPercent));
        _fanControlSafetyTracker.MarkOverrideActive(response.FanIndex);

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
        FrameworkRestoreAutoFanControlResponse response = connection.RestoreAutoFanControl(fanIndex);
        _fanControlSafetyTracker.MarkAutoRestored(response.FanIndex);

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
            snapshot = getSnapshot();
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
        uint? batteryCycleCount = null)
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

        foreach (var fanIndex in fansToRestore)
        {
            try
            {
                _connection.RestoreAutoFanControl(fanIndex);
                _fanControlSafetyTracker.CompleteRestore(fanIndex, restored: true);
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

                await Task.Delay(pollingInterval.Value, cancellationToken).ConfigureAwait(false);
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
}
