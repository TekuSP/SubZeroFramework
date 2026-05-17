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
using UnitsNet;

namespace SubZeroFramework.Services;

public sealed class FrameworkDataProvider : IFrameworkDataProvider, IDisposable
{
    private static readonly TimeSpan MaximumHistoryWindow = TimeSpan.FromHours(1);
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
    private readonly RetainedSnapshotStream<FrameworkSystemStatus> _systemStatus = new(MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<FrameworkEcFlashSnapshot> _flashSnapshots = new(MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<FrameworkFanCapabilitiesSnapshot> _fanCapabilitiesSnapshots = new(MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<FrameworkPowerSnapshot> _powerSnapshots = new(MaximumHistoryWindow, TelemetryScheduler);
    private readonly RetainedSnapshotStream<FrameworkThermalSnapshot> _thermalSnapshots = new(MaximumHistoryWindow, TelemetryScheduler);
    private readonly SourceCache<FanCapabilityState, int> _fanCapabilities = new(capability => capability.FanIndex);
    private readonly SourceCache<FanStateSnapshot, int> _fanStates = new(fanState => fanState.FanIndex);
    private readonly SourceCache<TelemetryChannel, TelemetryChannelId> _telemetryChannels = new(channel => channel.Id);
    private readonly SourceCache<CurrentTelemetryValue, TelemetryChannelId> _currentTelemetryValues = new(value => value.ChannelId);
    private readonly SourceCache<TelemetryPoint, long> _telemetryPoints = new(point => point.SampleId);
    private readonly RetainedSnapshotStream<HardwareInfoSnapshot> _hardwareInfoSnapshots = new(MaximumHistoryWindow, TelemetryScheduler);
    private readonly CompositeDisposable _subscriptions = [];
    private HardwareInfoSnapshot _latestHardwareInfoSnapshot = new()
    {
        ObservedAt = DateTimeOffset.MinValue,
        IsAvailable = false,
        LastError = "Hardware information has not been collected yet.",
    };
    private readonly IHardwareInfo _hardwareInfo;
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
    private DateTimeOffset _lastTelemetryObservedAt;
    private bool _isFanControlEnabled;
    private bool _hasCallerIdentityValidation;
    private string? _fanControlAuthorizationMessage;
    private bool _disposed;

    public FrameworkDataProvider(
        IFrameworkSystem frameworkSystem,
        IHardwareInfo hardwareInfo,
        ILogger<FrameworkDataProvider> logger)
    {
        _frameworkSystem = frameworkSystem;
        _hardwareInfo = hardwareInfo;
        _logger = logger;
        SystemStatus = _systemStatus;
        FlashSnapshots = _flashSnapshots;
        FanCapabilitiesSnapshots = _fanCapabilitiesSnapshots;
        PowerSnapshots = _powerSnapshots;
        ThermalSnapshots = _thermalSnapshots;
        HardwareInfoSnapshots = _hardwareInfoSnapshots;
        _telemetryPoints
            .ExpireAfter(_ => MaximumHistoryWindow, scheduler: TelemetryScheduler)
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
            .ExpireAfter(point => GetRemainingHistory(point.ObservedAt, validatedHistoryWindow), scheduler: TelemetryScheduler);
    }

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectTemperatureSeries(int sensorIndex, TimeSpan historyWindow)
        => ConnectTelemetrySeries(CreateTemperatureChannelId(sensorIndex), historyWindow);

    public IObservable<IChangeSet<TelemetryPoint, long>> ConnectFanSpeedSeries(int fanIndex, TimeSpan historyWindow)
        => ConnectTelemetrySeries(CreateFanSpeedChannelId(fanIndex), historyWindow);

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
            _hardwareInfo.RefreshCPUList(false, 500, true);
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
            _hardwareInfo.RefreshMonitorList();
            _hardwareInfo.RefreshVideoControllerList();
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
            if (_hardwareInfo.MonitorList.Count > 0)
            {
                monitors = _hardwareInfo.MonitorList
                    .Select(monitor => new HardwareInfoMonitor(
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
                        YearOfManufacture: monitor.YearOfManufacture))
                    .ToImmutableArray();
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read monitor data");
        }

        try
        {
            if (_hardwareInfo.VideoControllerList.Count > 0)
            {
                videoControllers = _hardwareInfo.VideoControllerList
                    .Select(video => new HardwareInfoVideoController(
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
                        VideoProcessor: video.VideoProcessor))
                    .ToImmutableArray();
            }
        }
        catch (Exception exception)
        {
            CaptureFailure(exception, "read video controller data");
        }

        try
        {
            if (_hardwareInfo.CpuList.Count > 0)
            {
                cpus = _hardwareInfo.CpuList
                    .Select(cpu => new HardwareInfoCpu(
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
                        PercentProcessorTime: null,
                        CpuCores: []))
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
            _systemStatus.Publish(systemStatus, systemStatus.ObservedAt);
            return systemStatus;
        }

        var connection = EnsureConnection();

        if (connection is null)
        {
            var unavailableStatus = systemStatus with { LastError = systemStatus.LastError ?? "Unable to open the default EC connection." };
            MarkAllTelemetryUnavailable(unavailableStatus.ObservedAt);
            _systemStatus.Publish(unavailableStatus, unavailableStatus.ObservedAt);
            return unavailableStatus;
        }

        var observedAt = DateTimeOffset.UtcNow;
        var connectedStatus = EnrichConnectionStatus(systemStatus with { ObservedAt = observedAt }, connection);

        var successfulReads = 0;
        string? snapshotError = null;

        if (TryReadSnapshot(connection.GetFlashSnapshot, "flash", ref snapshotError, out var flashSnapshot))
        {
            _flashSnapshots.Publish(flashSnapshot!, observedAt);
            successfulReads += 1;
        }

        if (TryReadSnapshot(connection.GetFanCapabilitiesSnapshot, "fan capabilities", ref snapshotError, out var fanCapabilitiesSnapshot))
        {
            _fanCapabilitiesSnapshots.Publish(fanCapabilitiesSnapshot!, observedAt);
            PublishFanCapabilities(fanCapabilitiesSnapshot!, observedAt);
            successfulReads += 1;
        }

        if (TryReadSnapshot(connection.GetPowerSnapshot, "power", ref snapshotError, out var powerSnapshot))
        {
            _powerSnapshots.Publish(powerSnapshot!, observedAt);
            PublishPowerTelemetry(powerSnapshot!, observedAt);
            successfulReads += 1;
        }

        if (TryReadSnapshot(connection.GetThermalSnapshot, "thermal", ref snapshotError, out var thermalSnapshot))
        {
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
        FrameworkSetFanDutyResponse response = connection.SetFanDuty(fanIndex, Ratio.FromPercent(dutyPercent));

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

        return Task.FromResult(new FrameworkRestoreAutoFanControlCommandResult
        {
            FanIndex = response.FanIndex,
        });
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

    private void PublishThermalTelemetry(FrameworkThermalSnapshot thermalSnapshot, DateTimeOffset observedAt)
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
                temperatureState: temperatureSnapshot.State);
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
            _fanStates.AddOrUpdate(new FanStateSnapshot
            {
                FanIndex = fanIndex,
                DisplayName = $"Fan {fanIndex}",
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
                displayName: $"Fan {fanIndex}",
                unitSymbol: "RPM",
                observedAt: observedAt,
                numericValue: fanSnapshot.Speed.RevolutionsPerMinute);

            fanIndex++;
        }

        var staleFanStates = _fanStates.Items
            .Where(fanState => !observedFanIndices.Contains(fanState.FanIndex))
            .ToArray();

        foreach (var staleFanState in staleFanStates)
        {
            _fanStates.AddOrUpdate(staleFanState with
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

    private void PublishFanCapabilities(FrameworkFanCapabilitiesSnapshot fanCapabilitiesSnapshot, DateTimeOffset observedAt)
    {
        var observedFanIndices = new HashSet<int>();

        for (var fanIndex = 0; fanIndex < fanCapabilitiesSnapshot.FanCount; fanIndex++)
        {
            observedFanIndices.Add(fanIndex);
            _fanCapabilities.AddOrUpdate(new FanCapabilityState
            {
                FanIndex = fanIndex,
                DisplayName = $"Fan {fanIndex}",
                Features = fanCapabilitiesSnapshot.Features,
                SupportsFanControl = fanCapabilitiesSnapshot.Features.HasFlag(FrameworkFanFeaturesState.FanControl),
                SupportsThermalReporting = fanCapabilitiesSnapshot.Features.HasFlag(FrameworkFanFeaturesState.ThermalReporting),
                ObservedAt = observedAt,
                IsAvailable = true,
            });
        }

        var staleCapabilities = _fanCapabilities.Items
            .Where(capability => !observedFanIndices.Contains(capability.FanIndex))
            .ToArray();

        foreach (var staleCapability in staleCapabilities)
        {
            _fanCapabilities.AddOrUpdate(staleCapability with
            {
                ObservedAt = observedAt,
                IsAvailable = false,
            });
        }
    }

    private void PublishPowerTelemetry(FrameworkPowerSnapshot powerSnapshot, DateTimeOffset observedAt)
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
        _currentTelemetryValues.AddOrUpdate(new CurrentTelemetryValue
        {
            ChannelId = channelId,
            DisplayName = displayName,
            UnitSymbol = unitSymbol,
            ObservedAt = observedAt,
            NumericValue = numericValue,
            TemperatureState = temperatureState,
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
        });

        _telemetryPoints.AddOrUpdate(new TelemetryPoint(
            SampleId: Interlocked.Increment(ref _nextTelemetryPointId),
            ChannelId: channelId,
            ObservedAt: observedAt,
            NumericValue: numericValue));
    }

    private void UpsertChannel(
        TelemetryChannelId channelId,
        string displayName,
        string unitSymbol,
        DateTimeOffset observedAt,
        bool isAvailable)
    {
        var existingChannel = _telemetryChannels.Items.FirstOrDefault(channel => channel.Id == channelId);

        if (existingChannel is null)
        {
            _telemetryChannels.AddOrUpdate(new TelemetryChannel
            {
                Id = channelId,
                DisplayName = displayName,
                UnitSymbol = unitSymbol,
                FirstObservedAt = observedAt,
                LastObservedAt = observedAt,
                IsAvailable = isAvailable,
            });
            return;
        }

        _telemetryChannels.AddOrUpdate(existingChannel with
        {
            DisplayName = displayName,
            UnitSymbol = unitSymbol,
            LastObservedAt = observedAt,
            IsAvailable = isAvailable,
        });
    }

    private void SetChannelsAvailability(
        TelemetryArea area,
        TelemetryEntityKind entityKind,
        IReadOnlySet<TelemetryChannelId> observedChannels,
        DateTimeOffset observedAt)
    {
        var staleChannels = _telemetryChannels.Items
            .Where(channel => channel.Id.Area == area && channel.Id.EntityKind == entityKind && !observedChannels.Contains(channel.Id))
            .ToArray();

        foreach (var staleChannel in staleChannels)
        {
            if (staleChannel.IsAvailable)
            {
                _telemetryChannels.AddOrUpdate(staleChannel with { IsAvailable = false });
            }

            _currentTelemetryValues.AddOrUpdate(new CurrentTelemetryValue
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
            });
        }
    }

    private void MarkAllTelemetryUnavailable(DateTimeOffset observedAt)
    {
        foreach (var channel in _telemetryChannels.Items.ToArray())
        {
            if (channel.IsAvailable)
            {
                _telemetryChannels.AddOrUpdate(channel with { IsAvailable = false });
            }

            _currentTelemetryValues.AddOrUpdate(new CurrentTelemetryValue
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

        MarkAllFanCapabilitiesUnavailable(observedAt);
        MarkAllFanStatesUnavailable(observedAt);
    }

    private void MarkAllFanCapabilitiesUnavailable(DateTimeOffset observedAt)
    {
        foreach (var fanCapability in _fanCapabilities.Items.ToArray())
        {
            if (!fanCapability.IsAvailable)
            {
                continue;
            }

            _fanCapabilities.AddOrUpdate(fanCapability with
            {
                ObservedAt = observedAt,
                IsAvailable = false,
            });
        }
    }

    private void MarkAllFanStatesUnavailable(DateTimeOffset observedAt)
    {
        foreach (var fanState in _fanStates.Items.ToArray())
        {
            if (!fanState.IsAvailable)
            {
                continue;
            }

            _fanStates.AddOrUpdate(fanState with
            {
                ObservedAt = observedAt,
                IsAvailable = false,
            });
        }
    }

    private void RestoreAutomaticFanControl()
    {
        if (_connection is null)
        {
            return;
        }

        var fanCount = _fanCapabilities.Items.Count(capability => capability.IsAvailable);
        for (var fanIndex = 0; fanIndex < fanCount; fanIndex++)
        {
            try
            {
                _connection.RestoreAutoFanControl(fanIndex);
            }
            catch (Exception exception)
            {
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
        if (historyWindow <= TimeSpan.Zero || historyWindow > MaximumHistoryWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(historyWindow), $"History window must be between {TimeSpan.Zero} and {MaximumHistoryWindow}.");
        }

        return historyWindow;
    }

    private static TimeSpan? GetRemainingHistory(DateTimeOffset observedAt, TimeSpan historyWindow)
    {
        var remainingLifetime = (observedAt + historyWindow) - TelemetryScheduler.Now;
        return remainingLifetime > TimeSpan.Zero ? remainingLifetime : TimeSpan.Zero;
    }

    private static TelemetryChannelId CreateTemperatureChannelId(int sensorIndex)
        => new(TelemetryArea.Thermal, TelemetryEntityKind.TemperatureSensor, sensorIndex, TelemetryMetric.TemperatureCelsius);

    private static TelemetryChannelId CreateFanSpeedChannelId(int fanIndex)
        => new(TelemetryArea.Thermal, TelemetryEntityKind.Fan, fanIndex, TelemetryMetric.FanSpeedRpm);

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
