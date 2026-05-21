using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using FrameworkDotnet.Enums;
using LiveChartsCore.Defaults;
using Microsoft.UI.Xaml;
using SubZeroFramework.Controls.Fans.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using CommunityToolkit.WinUI;
using SubZeroFramework.Controls.DeviceCapabilities.Models;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

public partial class DeviceCapabilitiesModel : ObservableObject, IDisposable
{
    private readonly IHardwareInfoClient _hardwareInfoClient;
    private readonly IFanStateClient _fanStateClient;
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly IFanCapabilityClient _fanCapabilityClient;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly IBatteryTelemetryClient _batteryTelemetryClient;
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly Dictionary<int, TemperatureTelemetrySnapshot> _temperatureSnapshots = [];
    private readonly Dictionary<int, FanCapabilityState> _fanCapabilities = [];
    private readonly Dictionary<int, FanTelemetrySnapshot> _fanSnapshots = [];
    private readonly Dictionary<int, FanStateSnapshot> _fanStateSnapshots = [];
    private readonly Dictionary<int, BatteryTelemetrySnapshot> _batterySnapshots = [];
    private readonly ObservableCollection<DeviceCapabilitiesCpuPackageCardModel> _cpuPackageCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMemoryModuleCardModel> _memoryModuleCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesStorageDriveCardModel> _storageDriveCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesNetworkAdapterCardModel> _networkAdapterCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesVideoControllerCardModel> _videoControllerCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesGraphicsCardGroupModel> _graphicsCardGroups = [];
    private readonly ObservableCollection<DeviceCapabilitiesMonitorCardModel> _monitorCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMonitorResolutionCard> _monitorResolutionCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _temperatureStatusItems = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _fanStatusItems = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _batteryStatusItems = [];
    private HistoricalRecord<HardwareInfoSnapshot>[] _cpuHistoryRecords = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(CpuCount),
        nameof(TotalCoreCount),
        nameof(TotalLogicalProcessorCount),
        nameof(AverageClockSpeed),
        nameof(AverageMaxClockSpeed),
        nameof(AverageCpuUsageDisplay),
        nameof(GraphicsAdapterCount),
        nameof(MonitorCount),
        nameof(ActiveMonitorCount),
        nameof(StorageDriveCount),
        nameof(TotalStorageCapacity),
        nameof(TotalStorageUsedSpace),
        nameof(TotalStorageFreeSpace),
        nameof(TotalStorageUsagePercent),
        nameof(TotalStorageUsageSummary),
        nameof(NetworkAdapterCount),
        nameof(ConnectedNetworkAdapterCount),
        nameof(MonitorResolutionCards),
        nameof(SystemProfileOs),
        nameof(SystemProfileVendor),
        nameof(SystemProfileModel),
        nameof(SystemProfileOsVersion),
        nameof(SystemProfileProductName),
        nameof(SystemProfileSystemVersion),
        nameof(SystemProfileBiosVersion),
        nameof(SystemProfileBiosReleaseDate),
        nameof(SystemProfileEcBuildInfo),
        nameof(MemoryModuleCount),
        nameof(MemoryTotalCapacity),
        nameof(TotalPhysicalMemory),
        nameof(AvailablePhysicalMemory),
        nameof(PhysicalMemoryUsagePercent),
        nameof(PhysicalMemoryUsageDisplay),
        nameof(PhysicalMemoryUsageTone),
        nameof(PhysicalMemoryUsageSuccessVisibility),
        nameof(PhysicalMemoryUsageWarningVisibility),
        nameof(PhysicalMemoryUsageErrorVisibility),
        nameof(TotalPageFileMemory),
        nameof(AvailablePageFileMemory),
        nameof(TotalVirtualMemory),
        nameof(AvailableVirtualMemory))]
    public partial HardwareInfoSnapshot? Snapshot { get; set; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesCpuPackageCardModel> CpuPackageCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMemoryModuleCardModel> MemoryModuleCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesStorageDriveCardModel> StorageDriveCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesNetworkAdapterCardModel> NetworkAdapterCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel> VideoControllerCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesGraphicsCardGroupModel> GraphicsCardGroups { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel> MonitorCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorResolutionCard> MonitorResolutionCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> TemperatureStatusItems { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> FanStatusItems { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> BatteryStatusItems { get; }

    public DeviceCapabilitiesOnboardStatusSectionModel OnboardStatusSection { get; }

    public DeviceCapabilitiesSystemProfileSectionModel SystemProfileSection { get; }

    public DeviceCapabilitiesCoolingSectionModel CoolingSection { get; }

    public DeviceCapabilitiesCpuSectionModel CpuSection { get; }

    public DeviceCapabilitiesStorageSectionModel StorageSection { get; }

    public DeviceCapabilitiesMemorySectionModel MemorySection { get; }

    public DeviceCapabilitiesPlatformFirmwareSectionModel PlatformFirmwareSection { get; }

    public DeviceCapabilitiesGraphicsSectionModel GraphicsSection { get; }

    public DeviceCapabilitiesNetworkSectionModel NetworkSection { get; }

    public int CpuCount => Snapshot?.Runtime.Cpus.Length ?? 0;

    public int TotalCoreCount => Snapshot?.Runtime.Cpus.Sum(cpu => cpu.Cores) ?? 0;

    public int TotalLogicalProcessorCount => Snapshot?.Runtime.Cpus.Sum(cpu => cpu.LogicalProcessors) ?? 0;

    public string AverageClockSpeed => Snapshot?.Runtime.Cpus.Length > 0
        ? $"{Snapshot.Runtime.Cpus.Average(cpu => cpu.CurrentClockSpeedMHz):0} MHz"
        : "Unknown";

    public string AverageMaxClockSpeed => Snapshot?.Runtime.Cpus.Length > 0
        ? $"{Snapshot.Runtime.Cpus.Average(cpu => cpu.MaxClockSpeedMHz):0} MHz"
        : "Unknown";

    public string AverageCpuUsageDisplay
    {
        get
        {
            var averageUsage = GetAverageCpuUsagePercent(Snapshot);
            return averageUsage is double value
                ? $"{Math.Clamp(value, 0d, 100d):0.#} %"
                : "Unknown";
        }
    }

    public int GraphicsAdapterCount => Snapshot?.Runtime.VideoControllers.Length ?? 0;

    public int MonitorCount => Snapshot?.Runtime.Monitors.Length is > 0
        ? Snapshot.Runtime.Monitors.Length
        : Snapshot?.Runtime.VideoControllers.Count(HasActiveDisplay) ?? 0;

    public int ActiveMonitorCount => Snapshot?.Runtime.Monitors.Length is > 0
        ? Snapshot.Runtime.Monitors.Count(monitor => monitor.Active)
        : Snapshot?.Runtime.VideoControllers.Count(HasActiveDisplay) ?? 0;

    public int StorageDriveCount => Snapshot?.Inventory.Drives.Length ?? 0;

    public string TotalStorageCapacity => Snapshot?.Inventory.Drives.Length > 0
        ? FormatBytes(Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.Size))
        : "Unknown";

    public string TotalStorageUsedSpace => Snapshot?.Inventory.Drives.Length > 0
        ? FormatBytes(Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.UsedSpace))
        : "Unknown";

    public string TotalStorageFreeSpace => Snapshot?.Inventory.Drives.Length > 0
        ? FormatBytes(Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.ClampedFreeSpace))
        : "Unknown";

    public double TotalStorageUsagePercent
    {
        get
        {
            if (Snapshot?.Inventory.Drives.Length is not > 0)
            {
                return 0d;
            }

            var totalCapacity = Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.Size);
            var totalUsed = Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.UsedSpace);

            return totalCapacity == 0
                ? 0d
                : Math.Clamp(totalUsed * 100d / totalCapacity, 0d, 100d);
        }
    }

    public string TotalStorageUsageSummary => Snapshot?.Inventory.Drives.Length > 0
        ? $"{TotalStorageUsedSpace} used / {TotalStorageFreeSpace} free"
        : "Unknown";

    public int NetworkAdapterCount => Snapshot?.Inventory.NetworkAdapters.Length ?? 0;

    public int ConnectedNetworkAdapterCount => Snapshot?.Inventory.NetworkAdapters.Count(adapter => adapter.HasAssignedAddress) ?? 0;

    public string SystemProfileOs => Snapshot?.Inventory.OperatingSystem?.Name ?? "Unknown";

    public string SystemProfileVendor => Snapshot?.Inventory.ComputerSystem?.Vendor ?? "Unknown";

    public string SystemProfileModel => FrameworkStatus?.DeviceModel
        ?? Snapshot?.Inventory.ComputerSystem?.Caption
        ?? "Unknown";

    public string SystemProfileOsVersion => Snapshot?.Inventory.OperatingSystem?.VersionString ?? "Unknown";

    public string SystemProfileProductName => GetSystemProfileProductName() ?? "Not separately reported";

    public string SystemProfileSystemVersion => Snapshot?.Inventory.ComputerSystem?.Version ?? "Unknown";

    public string SystemProfileBiosVersion => FirstNonEmpty(Snapshot?.Inventory.Bios?.Version) ?? "Unknown";

    public string SystemProfileBiosReleaseDate => FirstNonEmpty(Snapshot?.Inventory.Bios?.DisplayReleaseDate) ?? "Unknown";

    public string SystemProfileEcBuildInfo => FirstNonEmpty(FrameworkStatus?.EcBuildInfo)
        ?? "Unavailable";

    public int MemoryModuleCount => Snapshot?.Inventory.MemoryModules.Length ?? 0;

    public string MemoryTotalCapacity
    {
        get
        {
            if (Snapshot?.Inventory.MemoryModules.Length > 0)
            {
                var totalBytes = Snapshot.Inventory.MemoryModules.Sum(module => (long)module.CapacityBytes);
                return FormatBytes((ulong)totalBytes);
            }

            return "Unknown";
        }
    }

    public string TotalPhysicalMemory => Snapshot?.Runtime.MemoryStatus is null
        ? "Unknown"
        : FormatBytes(Snapshot.Runtime.MemoryStatus.TotalPhysical);

    public string AvailablePhysicalMemory => Snapshot?.Runtime.MemoryStatus is null
        ? "Unknown"
        : FormatBytes(Snapshot.Runtime.MemoryStatus.AvailablePhysical);

    public double PhysicalMemoryUsagePercent => Snapshot?.Runtime.MemoryStatus is { TotalPhysical: > 0 } memoryStatus
        ? Math.Clamp((memoryStatus.TotalPhysical - Math.Min(memoryStatus.AvailablePhysical, memoryStatus.TotalPhysical)) * 100d / memoryStatus.TotalPhysical, 0d, 100d)
        : 0d;

    public string PhysicalMemoryUsageDisplay
    {
        get
        {
            if (Snapshot?.Runtime.MemoryStatus is not { TotalPhysical: > 0 } memoryStatus)
            {
                return "Unknown";
            }

            var usedBytes = memoryStatus.TotalPhysical - Math.Min(memoryStatus.AvailablePhysical, memoryStatus.TotalPhysical);
            return $"{PhysicalMemoryUsagePercent:N0}% used ({FormatBytes(usedBytes)} / {FormatBytes(memoryStatus.TotalPhysical)})";
        }
    }

    public DeviceCapabilitiesStatusTone PhysicalMemoryUsageTone => PhysicalMemoryUsagePercent switch
    {
        >= PresentationDefaults.ErrorUsagePercent => DeviceCapabilitiesStatusTone.Error,
        >= PresentationDefaults.WarningUsagePercent => DeviceCapabilitiesStatusTone.Warning,
        _ => DeviceCapabilitiesStatusTone.Success,
    };

    public Visibility PhysicalMemoryUsageSuccessVisibility => PhysicalMemoryUsageTone == DeviceCapabilitiesStatusTone.Success
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PhysicalMemoryUsageWarningVisibility => PhysicalMemoryUsageTone == DeviceCapabilitiesStatusTone.Warning
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PhysicalMemoryUsageErrorVisibility => PhysicalMemoryUsageTone == DeviceCapabilitiesStatusTone.Error
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string TotalPageFileMemory => Snapshot?.Runtime.MemoryStatus is null
        ? "Unknown"
        : FormatBytes(Snapshot.Runtime.MemoryStatus.TotalPageFile);

    public string AvailablePageFileMemory => Snapshot?.Runtime.MemoryStatus is null
        ? "Unknown"
        : FormatBytes(Snapshot.Runtime.MemoryStatus.AvailablePageFile);

    public string TotalVirtualMemory => Snapshot?.Runtime.MemoryStatus is null
        ? "Unknown"
        : FormatBytes(Snapshot.Runtime.MemoryStatus.TotalVirtual);

    public string AvailableVirtualMemory => Snapshot?.Runtime.MemoryStatus is null
        ? "Unknown"
        : FormatBytes(Snapshot.Runtime.MemoryStatus.AvailableVirtual);

    [ObservableProperty]
    public partial DateTimePoint[] CpuUsageHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] CpuUsageHistorySeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? CpuUsageHistoryMinLimit { get; set; }

    [ObservableProperty]
    public partial double? CpuUsageHistoryMaxLimit { get; set; }

    public Func<DateTime, string> CpuUsageLabelsFormatter => FormatCpuClockLabel;

    [ObservableProperty]
    public partial DateTimePoint[] CpuClockHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] CpuClockHistorySeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? CpuClockHistoryMinLimit { get; set; }

    [ObservableProperty]
    public partial double? CpuClockHistoryMaxLimit { get; set; }

    public Func<DateTime, string> CpuClockLabelsFormatter => FormatCpuClockLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemProfileModel), nameof(SystemProfileProductName), nameof(SystemProfileEcBuildInfo))]
    public partial FrameworkSystemStatus? FrameworkStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoolingHardwareVisibility))]
    public partial FanAdvancedInfoCardModel? FanAdvancedInfo { get; set; }

    public Visibility CoolingHardwareVisibility => FanAdvancedInfo is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public DeviceCapabilitiesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IHardwareInfoClient hardwareInfoClient,
        IFanCapabilityClient fanCapabilityClient,
        IFanStateClient fanStateClient,
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        IFrameworkStatusClient frameworkStatusClient,
        SynchronizationContext synchronizationContext,
        DispatcherQueue dispatcherQueue)
    {
        _hardwareInfoClient = hardwareInfoClient;
        _fanStateClient = fanStateClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _batteryTelemetryClient = batteryTelemetryClient;
        _frameworkStatusClient = frameworkStatusClient;
        _synchronizationContext = synchronizationContext;
        _fanCapabilityClient = fanCapabilityClient;
        _dispatcherQueue = dispatcherQueue;

        CpuPackageCards = new ReadOnlyObservableCollection<DeviceCapabilitiesCpuPackageCardModel>(_cpuPackageCards);
        MemoryModuleCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMemoryModuleCardModel>(_memoryModuleCards);
        StorageDriveCards = new ReadOnlyObservableCollection<DeviceCapabilitiesStorageDriveCardModel>(_storageDriveCards);
        NetworkAdapterCards = new ReadOnlyObservableCollection<DeviceCapabilitiesNetworkAdapterCardModel>(_networkAdapterCards);
        VideoControllerCards = new ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel>(_videoControllerCards);
        GraphicsCardGroups = new ReadOnlyObservableCollection<DeviceCapabilitiesGraphicsCardGroupModel>(_graphicsCardGroups);
        MonitorCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel>(_monitorCards);
        MonitorResolutionCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMonitorResolutionCard>(_monitorResolutionCards);
        TemperatureStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_temperatureStatusItems);
        FanStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_fanStatusItems);
        BatteryStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_batteryStatusItems);

        OnboardStatusSection = new DeviceCapabilitiesOnboardStatusSectionModel(this);
        SystemProfileSection = new DeviceCapabilitiesSystemProfileSectionModel(this);
        CoolingSection = new DeviceCapabilitiesCoolingSectionModel(this);
        CpuSection = new DeviceCapabilitiesCpuSectionModel(this);
        StorageSection = new DeviceCapabilitiesStorageSectionModel(this);
        MemorySection = new DeviceCapabilitiesMemorySectionModel(this);
        PlatformFirmwareSection = new DeviceCapabilitiesPlatformFirmwareSectionModel(this);
        GraphicsSection = new DeviceCapabilitiesGraphicsSectionModel(this);
        NetworkSection = new DeviceCapabilitiesNetworkSectionModel(this);

        _hardwareInfoClient
            .WatchHardwareInfo()
            .ObserveOn(_synchronizationContext)
            .Select(snapshot => Observable.FromAsync(() => UpdateSnapshotAsync(snapshot)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);

        _hardwareInfoClient
            .WatchHardwareInfoHistory(TelemetryHistoryLimits.MaximumHistoryWindow)
            .ObserveOn(_synchronizationContext)
            .ToCollection()
            .Select(history => Observable.FromAsync(() => UpdateCpuClockHistoryAsync(history)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);

        _frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Select(status => Observable.FromAsync(() => UpdateFrameworkStatusAsync(status)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);

        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Select(set => Observable.FromAsync(() => ApplyTemperatureChangesAsync(set)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);

        _fanCapabilityClient
            .WatchFanCapabilities()
            .ObserveOn(_synchronizationContext)
            .Select(set => Observable.FromAsync(() => ApplyFanCapabilityChangesAsync(set)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);

        _fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Select(set => Observable.FromAsync(() => ApplyFanTelemetryChangesAsync(set)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);

        _fanStateClient
            .WatchFanStates()
            .ObserveOn(_synchronizationContext)
            .Select(set => Observable.FromAsync(() => ApplyFanStateChangesAsync(set)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);

        _batteryTelemetryClient
            .WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .Select(set => Observable.FromAsync(() => ApplyBatteryChangesAsync(set)))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);
    }

    private async Task UpdateSnapshotAsync(HardwareInfoSnapshot snapshot)
    {
        await _dispatcherQueue.EnqueueAsync(() => Snapshot = snapshot);
        await RefreshSnapshotCardsAsync();
        await RefreshCpuVisualsAsync();
    }

    private async Task UpdateCpuClockHistoryAsync(IReadOnlyCollection<HistoricalRecord<HardwareInfoSnapshot>> history)
    {
        _cpuHistoryRecords = [
            .. history
                .OrderBy(record => record.ObservedAt)
                .ThenBy(record => record.SampleId)
        ];

        await RefreshCpuVisualsAsync();
    }

    private Task UpdateFrameworkStatusAsync(FrameworkSystemStatus status)
        => _dispatcherQueue.EnqueueAsync(() => FrameworkStatus = status);

    private async Task ApplyTemperatureChangesAsync(IChangeSet<TemperatureTelemetrySnapshot, int> set)
    {
        ApplySnapshotChanges(_temperatureSnapshots, set);
        await RefreshTemperatureStatusItemsAsync();
    }

    private async Task ApplyFanCapabilityChangesAsync(IChangeSet<FanCapabilityState, int> set)
    {
        ApplySnapshotChanges(_fanCapabilities, set);
        await RefreshFanAdvancedInfoCardAsync();
    }

    private async Task ApplyFanTelemetryChangesAsync(IChangeSet<FanTelemetrySnapshot, int> set)
    {
        ApplySnapshotChanges(_fanSnapshots, set);
        await RefreshFanStatusItemsAsync();
    }

    private async Task ApplyFanStateChangesAsync(IChangeSet<FanStateSnapshot, int> set)
    {
        ApplySnapshotChanges(_fanStateSnapshots, set);
        await RefreshFanStatusItemsAsync();
    }

    private async Task ApplyBatteryChangesAsync(IChangeSet<BatteryTelemetrySnapshot, int> set)
    {
        ApplySnapshotChanges(_batterySnapshots, set);
        await RefreshBatteryStatusItemsAsync();
    }

    private async Task RefreshCpuVisualsAsync()
    {
        var cpuUsageHistory = BuildCpuUsageHistory();
        var (usageAxisStartTicks, usageAxisEndTicks, usageSeparators) = BuildCpuUsageHistoryAxis(cpuUsageHistory);
        var cpuClockHistory = BuildCpuClockHistory();
        var (axisStartTicks, axisEndTicks, separators) = BuildCpuClockHistoryAxis(cpuClockHistory);
        var cpuPackageUsageHistories = BuildCpuPackageUsageHistories();
        var cpuPackageClockHistories = BuildCpuPackageClockHistories();
        var cpuCoreUsageHistories = BuildCpuCoreUsageHistories();

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            CpuUsageHistory = cpuUsageHistory;
            CpuUsageHistoryMinLimit = usageAxisStartTicks;
            CpuUsageHistoryMaxLimit = usageAxisEndTicks;
            CpuUsageHistorySeparators = usageSeparators;
            CpuClockHistory = cpuClockHistory;
            CpuClockHistoryMinLimit = axisStartTicks;
            CpuClockHistoryMaxLimit = axisEndTicks;
            CpuClockHistorySeparators = separators;

            for (var packageIndex = 0; packageIndex < _cpuPackageCards.Count; packageIndex++)
            {
                var packageCard = _cpuPackageCards[packageIndex];
                var packageUsageHistory = cpuPackageUsageHistories.GetValueOrDefault(packageIndex) ?? [];
                var (packageUsageMinLimit, packageUsageMaxLimit, packageUsageSeparators) = BuildCpuUsageHistoryAxis(packageUsageHistory);
                packageCard.UpdateCpuUsageHistory(
                    packageUsageHistory,
                    packageUsageMinLimit,
                    packageUsageMaxLimit,
                    packageUsageSeparators);

                var packageClockHistory = cpuPackageClockHistories.GetValueOrDefault(packageIndex) ?? [];
                var (packageClockMinLimit, packageClockMaxLimit, packageClockSeparators) = BuildCpuClockHistoryAxis(packageClockHistory);
                packageCard.UpdateCpuClockHistory(
                    packageClockHistory,
                    packageClockMinLimit,
                    packageClockMaxLimit,
                    packageClockSeparators);

                for (var coreIndex = 0; coreIndex < packageCard.CpuCoreItems.Count; coreIndex++)
                {
                    packageCard.UpdateCpuCoreUsageHistory(
                        coreIndex,
                        cpuCoreUsageHistories.GetValueOrDefault((packageIndex, coreIndex)) ?? [],
                        usageAxisStartTicks,
                        usageAxisEndTicks,
                        usageSeparators);
                }
            }
        });
    }

    private Task RefreshFanAdvancedInfoCardAsync()
    {
        var coolingDetails = _fanCapabilities.Values
            .OrderByDescending(capability => capability.IsAvailable)
            .ThenBy(capability => capability.FanIndex)
            .Select(capability => capability.CoolingDetails)
            .FirstOrDefault(details => details is not null);

        return _dispatcherQueue.EnqueueAsync(() =>
        {
            switch (coolingDetails)
            {
                case null:
                    FanAdvancedInfo = null;
                    return;
                case FrameworkLaptop12CoolingDetails details:
                {
                    if (FanAdvancedInfo is not FrameworkLaptop12FanAdvancedInfoCardModel model)
                    {
                        model = new FrameworkLaptop12FanAdvancedInfoCardModel();
                        FanAdvancedInfo = model;
                    }

                    model.UpdateFrom(details);
                    return;
                }
                case FrameworkLaptop13CoolingDetails details:
                {
                    if (FanAdvancedInfo is not FrameworkLaptop13FanAdvancedInfoCardModel model)
                    {
                        model = new FrameworkLaptop13FanAdvancedInfoCardModel();
                        FanAdvancedInfo = model;
                    }

                    model.UpdateFrom(details);
                    return;
                }
                case FrameworkLaptop16CoolingDetails details:
                {
                    if (FanAdvancedInfo is not FrameworkLaptop16FanAdvancedInfoCardModel model)
                    {
                        model = new FrameworkLaptop16FanAdvancedInfoCardModel();
                        FanAdvancedInfo = model;
                    }

                    model.UpdateFrom(details);
                    return;
                }
                case FrameworkDesktopCoolingDetails details:
                {
                    if (FanAdvancedInfo is not FrameworkDesktopFanAdvancedInfoCardModel model)
                    {
                        model = new FrameworkDesktopFanAdvancedInfoCardModel();
                        FanAdvancedInfo = model;
                    }

                    model.UpdateFrom(details);
                    return;
                }
                default:
                    FanAdvancedInfo = null;
                    return;
            }
        });
    }

    private async Task RefreshSnapshotCardsAsync()
    {
        await SynchronizeCpuPackageCardsAsync(Snapshot?.Runtime.Cpus ?? []);
        await SynchronizeMemoryModuleCardsAsync(Snapshot?.Inventory.MemoryModules ?? []);
        await SynchronizeStorageDriveCardsAsync(Snapshot?.Inventory.Drives ?? []);
        await SynchronizeNetworkAdapterCardsAsync(Snapshot?.Inventory.NetworkAdapters ?? []);
        await SynchronizeVideoControllerCardsAsync(Snapshot?.Runtime.VideoControllers ?? []);
        await SynchronizeMonitorCardsAsync(Snapshot?.Runtime.Monitors ?? []);
        await SynchronizeGraphicsCardGroupsAsync();
        await SynchronizeMonitorResolutionCardsAsync(BuildMonitorResolutionCardData());
    }

    private DateTimePoint[] BuildCpuClockHistory()
    {
        List<DateTimePoint> points = [
            .. _cpuHistoryRecords
                .Where(record => record.Value.IsAvailable && record.Value.Runtime.Cpus.Length > 0)
                .Select(record => new DateTimePoint(
                    record.ObservedAt.LocalDateTime,
                    record.Value.Runtime.Cpus.Average(cpu => cpu.CurrentClockSpeedMHz)))
        ];

        if (Snapshot?.IsAvailable == true && Snapshot.Runtime.Cpus.Length > 0)
        {
            var observedAt = Snapshot.ObservedAt.LocalDateTime;
            if (points.Count == 0 || observedAt > points[^1].DateTime)
            {
                points.Add(new DateTimePoint(
                    observedAt,
                    Snapshot.Runtime.Cpus.Average(cpu => cpu.CurrentClockSpeedMHz)));
            }
        }

        return [.. points];
    }

    private DateTimePoint[] BuildCpuUsageHistory()
    {
        List<DateTimePoint> points = [
            .. _cpuHistoryRecords
                .Where(record => record.Value.IsAvailable && record.Value.Runtime.Cpus.Length > 0)
                .Select(record => new DateTimePoint(
                    record.ObservedAt.LocalDateTime,
                    GetAverageCpuUsagePercent(record.Value)))
        ];

        if (Snapshot?.IsAvailable == true && Snapshot.Runtime.Cpus.Length > 0)
        {
            var observedAt = Snapshot.ObservedAt.LocalDateTime;
            if (points.Count == 0 || observedAt > points[^1].DateTime)
            {
                points.Add(new DateTimePoint(observedAt, GetAverageCpuUsagePercent(Snapshot)));
            }
        }

        return TrimHistoryToRecentWindow(points);
    }

    private Dictionary<(int PackageIndex, int CoreIndex), DateTimePoint[]> BuildCpuCoreUsageHistories()
    {
        Dictionary<(int PackageIndex, int CoreIndex), List<DateTimePoint>> pointsByCore = [];

        foreach (var record in _cpuHistoryRecords.Where(record => record.Value.IsAvailable))
        {
            AppendCpuCoreHistoryPoints(pointsByCore, record.Value, record.ObservedAt.LocalDateTime);
        }

        if (Snapshot?.IsAvailable == true)
        {
            var observedAt = Snapshot.ObservedAt.LocalDateTime;
            if (_cpuHistoryRecords.Length == 0 || observedAt > _cpuHistoryRecords[^1].ObservedAt.LocalDateTime)
            {
                AppendCpuCoreHistoryPoints(pointsByCore, Snapshot, observedAt);
            }
        }

        return pointsByCore.ToDictionary(
            pair => pair.Key,
            pair => TrimHistoryToRecentWindow(pair.Value));
    }

    private Dictionary<int, DateTimePoint[]> BuildCpuPackageUsageHistories()
    {
        Dictionary<int, List<DateTimePoint>> pointsByPackage = [];

        foreach (var record in _cpuHistoryRecords.Where(record => record.Value.IsAvailable))
        {
            AppendCpuPackageUsageHistoryPoints(pointsByPackage, record.Value, record.ObservedAt.LocalDateTime);
        }

        if (Snapshot?.IsAvailable == true)
        {
            var observedAt = Snapshot.ObservedAt.LocalDateTime;
            if (_cpuHistoryRecords.Length == 0 || observedAt > _cpuHistoryRecords[^1].ObservedAt.LocalDateTime)
            {
                AppendCpuPackageUsageHistoryPoints(pointsByPackage, Snapshot, observedAt);
            }
        }

        return pointsByPackage.ToDictionary(
            pair => pair.Key,
            pair => TrimHistoryToRecentWindow(pair.Value));
    }

    private Dictionary<int, DateTimePoint[]> BuildCpuPackageClockHistories()
    {
        Dictionary<int, List<DateTimePoint>> pointsByPackage = [];

        foreach (var record in _cpuHistoryRecords.Where(record => record.Value.IsAvailable))
        {
            AppendCpuPackageClockHistoryPoints(pointsByPackage, record.Value, record.ObservedAt.LocalDateTime);
        }

        if (Snapshot?.IsAvailable == true)
        {
            var observedAt = Snapshot.ObservedAt.LocalDateTime;
            if (_cpuHistoryRecords.Length == 0 || observedAt > _cpuHistoryRecords[^1].ObservedAt.LocalDateTime)
            {
                AppendCpuPackageClockHistoryPoints(pointsByPackage, Snapshot, observedAt);
            }
        }

        return pointsByPackage.ToDictionary(
            pair => pair.Key,
            pair => (DateTimePoint[])[.. pair.Value]);
    }

    private (double? AxisStartTicks, double? AxisEndTicks, double[] Separators) BuildCpuClockHistoryAxis(DateTimePoint[] cpuClockHistory)
    {
        var historyPoints = cpuClockHistory
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var (axisStart, axisEnd, separators) = TimeChartAxisHelper.BuildAxis(
            historyPoints,
            PresentationDefaults.RecentTelemetryHistoryWindow,
            TimeChartAxisHelper.StandardLongSpanSeparatorStep);

        return (axisStart.Ticks, axisEnd.Ticks, separators);
    }

    private (double? AxisStartTicks, double? AxisEndTicks, double[] Separators) BuildCpuUsageHistoryAxis(DateTimePoint[] cpuUsageHistory)
    {
        var historyPoints = cpuUsageHistory
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var (axisStart, axisEnd, separators) = TimeChartAxisHelper.BuildAxis(
            historyPoints,
            PresentationDefaults.RecentTelemetryHistoryWindow,
            TimeChartAxisHelper.StandardLongSpanSeparatorStep);

        return (axisStart.Ticks, axisEnd.Ticks, separators);
    }

    private static double? GetAverageCpuUsagePercent(HardwareInfoSnapshot? snapshot)
    {
        if (snapshot?.IsAvailable != true || snapshot.Runtime.Cpus.Length == 0)
        {
            return null;
        }

        var cpuUsageValues = snapshot.Runtime.Cpus
            .Select(cpu => cpu.EffectivePercentProcessorTime)
            .Where(value => value is not null)
            .Select(value => Math.Clamp(value!.Value, 0d, 100d))
            .ToArray();

        return cpuUsageValues.Length == 0
            ? null
            : cpuUsageValues.Average();
    }

    private static DateTimePoint[] TrimHistoryToRecentWindow(IReadOnlyList<DateTimePoint> points)
    {
        if (points.Count == 0)
        {
            return [];
        }

        var windowEnd = points[^1].DateTime;
        var windowStart = windowEnd - PresentationDefaults.RecentTelemetryHistoryWindow;

        return [
            .. points
                .Where(point => point.DateTime >= windowStart)
        ];
    }

    private static void AppendCpuCoreHistoryPoints(
        IDictionary<(int PackageIndex, int CoreIndex), List<DateTimePoint>> pointsByCore,
        HardwareInfoSnapshot snapshot,
        DateTime observedAt)
    {
        for (var packageIndex = 0; packageIndex < snapshot.Runtime.Cpus.Length; packageIndex++)
        {
            var cpu = snapshot.Runtime.Cpus[packageIndex];
            for (var coreIndex = 0; coreIndex < cpu.CpuCores.Length; coreIndex++)
            {
                var key = (packageIndex, coreIndex);
                if (!pointsByCore.TryGetValue(key, out var history))
                {
                    history = [];
                    pointsByCore[key] = history;
                }

                history.Add(new DateTimePoint(
                    observedAt,
                    Math.Clamp(cpu.CpuCores[coreIndex].PercentProcessorTime, 0d, 100d)));
            }
        }
    }

    private static void AppendCpuPackageUsageHistoryPoints(
        IDictionary<int, List<DateTimePoint>> pointsByPackage,
        HardwareInfoSnapshot snapshot,
        DateTime observedAt)
    {
        for (var packageIndex = 0; packageIndex < snapshot.Runtime.Cpus.Length; packageIndex++)
        {
            var usageValue = snapshot.Runtime.Cpus[packageIndex].EffectivePercentProcessorTime;
            if (!pointsByPackage.TryGetValue(packageIndex, out var history))
            {
                history = [];
                pointsByPackage[packageIndex] = history;
            }

            history.Add(new DateTimePoint(
                observedAt,
                usageValue is null ? null : Math.Clamp(usageValue.Value, 0d, 100d)));
        }
    }

    private static void AppendCpuPackageClockHistoryPoints(
        IDictionary<int, List<DateTimePoint>> pointsByPackage,
        HardwareInfoSnapshot snapshot,
        DateTime observedAt)
    {
        for (var packageIndex = 0; packageIndex < snapshot.Runtime.Cpus.Length; packageIndex++)
        {
            if (!pointsByPackage.TryGetValue(packageIndex, out var history))
            {
                history = [];
                pointsByPackage[packageIndex] = history;
            }

            history.Add(new DateTimePoint(
                observedAt,
                snapshot.Runtime.Cpus[packageIndex].CurrentClockSpeedMHz));
        }
    }

    private string FormatCpuClockLabel(DateTime date)
    {
        var elapsed = DateTime.Now - date;

        if (elapsed.TotalSeconds < 1d)
        {
            return "now";
        }

        if (elapsed.TotalMinutes < 1d)
        {
            return $"{elapsed.TotalSeconds:N0} s ago";
        }

        if (elapsed.TotalHours < 1d)
        {
            return $"{elapsed.TotalMinutes:N0} m ago";
        }

        var hours = (int)Math.Floor(elapsed.TotalHours);
        var minutes = (int)Math.Round(elapsed.TotalMinutes - (hours * 60d), MidpointRounding.AwayFromZero);

        if (minutes == 60)
        {
            hours++;
            minutes = 0;
        }

        return minutes == 0
            ? $"{hours} h ago"
            : $"{hours} h {minutes} m ago";
    }

    private Task SynchronizeCpuPackageCardsAsync(IReadOnlyList<HardwareInfoCpu> cpus)
    {
        return SynchronizeCards(
            _cpuPackageCards,
            cpus,
            static (index, cpu) => new DeviceCapabilitiesCpuPackageCardModel(index, cpu),
            static (card, index, cpu) =>
            {
                card.Index = index;
                card.Snapshot = cpu;
            });
    }

    private Task SynchronizeMemoryModuleCardsAsync(IReadOnlyList<HardwareInfoMemoryModule> memoryModules)
    {
        return SynchronizeCards(
            _memoryModuleCards,
            memoryModules,
            static (_, memoryModule) => new DeviceCapabilitiesMemoryModuleCardModel(memoryModule),
            static (card, _, memoryModule) => card.Snapshot = memoryModule);
    }

    private Task SynchronizeStorageDriveCardsAsync(IReadOnlyList<HardwareInfoDrive> drives)
    {
        return SynchronizeCards(
            _storageDriveCards,
            drives,
            static (_, drive) => new DeviceCapabilitiesStorageDriveCardModel(drive),
            static (card, _, drive) => card.Snapshot = drive);
    }

    private Task SynchronizeNetworkAdapterCardsAsync(IReadOnlyList<HardwareInfoNetworkAdapter> networkAdapters)
    {
        return SynchronizeCards(
            _networkAdapterCards,
            networkAdapters,
            static (_, networkAdapter) => new DeviceCapabilitiesNetworkAdapterCardModel(networkAdapter),
            static (card, _, networkAdapter) => card.Snapshot = networkAdapter);
    }

    private Task SynchronizeVideoControllerCardsAsync(IReadOnlyList<HardwareInfoVideoController> videoControllers)
    {
        return SynchronizeCards(
            _videoControllerCards,
            videoControllers,
            static (_, videoController) => new DeviceCapabilitiesVideoControllerCardModel(videoController),
            static (card, _, videoController) => card.Snapshot = videoController);
    }

    private Task SynchronizeMonitorCardsAsync(IReadOnlyList<HardwareInfoMonitor> monitors)
    {
        return SynchronizeCards(
            _monitorCards,
            monitors,
            static (_, monitor) => new DeviceCapabilitiesMonitorCardModel(monitor),
            static (card, _, monitor) => card.Snapshot = monitor);
    }

    private async Task SynchronizeGraphicsCardGroupsAsync()
    {
        Dictionary<int, List<DeviceCapabilitiesMonitorCardModel>> monitorCardsByControllerIndex = [];
        for (var controllerIndex = 0; controllerIndex < _videoControllerCards.Count; controllerIndex++)
        {
            monitorCardsByControllerIndex[controllerIndex] = [];
        }

        List<DeviceCapabilitiesMonitorCardModel> unknownMonitorCards = [];
        foreach (var monitorCard in _monitorCards)
        {
            var linkedControllerIndex = FindLinkedVideoControllerCardIndex(monitorCard.Snapshot);
            if (linkedControllerIndex is int controllerIndex)
            {
                monitorCardsByControllerIndex[controllerIndex].Add(monitorCard);
                continue;
            }

            unknownMonitorCards.Add(monitorCard);
        }

        List<(DeviceCapabilitiesVideoControllerCardModel? VideoController, bool IsUnknownGraphicsCard, IReadOnlyList<DeviceCapabilitiesMonitorCardModel> MonitorCards)> groups = [];

        for (var controllerIndex = 0; controllerIndex < _videoControllerCards.Count; controllerIndex++)
        {
            var controllerCard = _videoControllerCards[controllerIndex];
            groups.Add((controllerCard, false, monitorCardsByControllerIndex[controllerIndex]));
        }

        if (unknownMonitorCards.Count > 0)
        {
            groups.Add((null, true, unknownMonitorCards));
        }

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            if (index < _graphicsCardGroups.Count)
            {
                await _dispatcherQueue.EnqueueAsync(() =>
                {
                    _graphicsCardGroups[index].IsUnknownGraphicsCard = group.IsUnknownGraphicsCard;
                    _graphicsCardGroups[index].VideoController = group.VideoController;
                    _graphicsCardGroups[index].SynchronizeMonitorCards(group.MonitorCards);
                });

                continue;
            }

            await _dispatcherQueue.EnqueueAsync(() =>
            {
                var graphicsCardGroup = new DeviceCapabilitiesGraphicsCardGroupModel(group.IsUnknownGraphicsCard, group.VideoController);
                graphicsCardGroup.SynchronizeMonitorCards(group.MonitorCards);
                _graphicsCardGroups.Add(graphicsCardGroup);
            });
        }

        while (_graphicsCardGroups.Count > groups.Count)
        {
            await _dispatcherQueue.EnqueueAsync(() => _graphicsCardGroups.RemoveAt(_graphicsCardGroups.Count - 1));
        }
    }

    public string FormatBytes(ulong bytes)
    {
        const double OneTerabyte = 1024d * 1024d * 1024d * 1024d;
        const double OneGigabyte = 1024d * 1024d * 1024d;
        if (bytes == 0)
        {
            return "0 GB";
        }

        if (bytes >= OneTerabyte)
        {
            return $"{bytes / OneTerabyte:0.##} TB";
        }

        return bytes >= OneGigabyte
            ? $"{bytes / OneGigabyte:0.##} GB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }

    private Task SynchronizeMonitorResolutionCardsAsync((string Title, string ResolutionTier)[] cards)
    {
        return SynchronizeCards(
            _monitorResolutionCards,
            cards,
            static (_, card) => new DeviceCapabilitiesMonitorResolutionCard(card.Title, card.ResolutionTier),
            static (existingCard, _, card) =>
            {
                existingCard.Title = card.Title;
                existingCard.ResolutionTier = card.ResolutionTier;
            });
    }

    private Task RefreshTemperatureStatusItemsAsync()
    {
        var sensors = _temperatureSnapshots.Values
            .OrderBy(snapshot => snapshot.SensorIndex)
            .ToArray();

        return SynchronizeCards(
            _temperatureStatusItems,
            sensors,
            (_, snapshot) => new DeviceCapabilitiesRuntimeStatusItemModel(
                $"Sensor {snapshot.SensorIndex}",
                GetTemperatureStatus(snapshot),
                GetStatusTone(GetTemperatureStatus(snapshot))),
            (card, _, snapshot) =>
            {
                card.Name = $"Sensor {snapshot.SensorIndex}";
                card.Status = GetTemperatureStatus(snapshot);
                card.StatusTone = GetStatusTone(card.Status);
            });
    }

    private Task RefreshFanStatusItemsAsync()
    {
        var fanEntries = _fanSnapshots.Keys
            .Union(_fanStateSnapshots.Keys)
            .OrderBy(index => index)
            .Select(index => (
                Index: index,
                Snapshot: _fanSnapshots.GetValueOrDefault(index),
                State: _fanStateSnapshots.GetValueOrDefault(index)))
            .ToArray();

        return SynchronizeCards(
            _fanStatusItems,
            fanEntries,
            (_, fan) => new DeviceCapabilitiesRuntimeStatusItemModel(
                $"Fan {fan.Index}",
                GetFanStatus(fan.Snapshot, fan.State),
                GetStatusTone(GetFanStatus(fan.Snapshot, fan.State))),
            (card, _, fan) =>
            {
                card.Name = $"Fan {fan.Index}";
                card.Status = GetFanStatus(fan.Snapshot, fan.State);
                card.StatusTone = GetStatusTone(card.Status);
            });
    }

    private Task RefreshBatteryStatusItemsAsync()
    {
        var batteries = _batterySnapshots.Values
            .OrderBy(snapshot => snapshot.BatteryIndex)
            .ToArray();

        return SynchronizeCards(
            _batteryStatusItems,
            batteries,
            (_, snapshot) => new DeviceCapabilitiesRuntimeStatusItemModel(
                $"Battery {snapshot.BatteryIndex}",
                GetBatteryStatus(snapshot),
                GetStatusTone(GetBatteryStatus(snapshot))),
            (card, _, snapshot) =>
            {
                card.Name = $"Battery {snapshot.BatteryIndex}";
                card.Status = GetBatteryStatus(snapshot);
                card.StatusTone = GetStatusTone(card.Status);
            });
    }

    private (string Title, string ResolutionTier)[] BuildMonitorResolutionCardData()
    {
        var monitors = Snapshot?.Runtime.Monitors ?? [];
        if (monitors.Length == 0)
        {
            var activeControllers = Snapshot?.Runtime.VideoControllers
                .Where(HasActiveDisplay)
                .ToArray()
                ?? [];

            return [
                .. activeControllers.Select((controller, index) => (
                    Title: FirstNonEmpty(controller.Name, controller.Caption, controller.Description) ?? $"Adapter {index}",
                    ResolutionTier: GetDisplayTierLabel(controller)))
            ];
        }

        return [
            .. monitors.Select(monitor => (
                Title: monitor.DisplayName,
                ResolutionTier: BuildMonitorModeSummary(monitor)))
        ];
    }

    private bool HasActiveDisplay(HardwareInfoVideoController controller)
    {
        return controller.CurrentHorizontalResolution > 0 && controller.CurrentVerticalResolution > 0;
    }

    private string GetDisplayTierLabel(HardwareInfoVideoController controller)
    {
        if (!HasActiveDisplay(controller))
        {
            return "Inactive";
        }

        var maxDimension = Math.Max(controller.CurrentHorizontalResolution, controller.CurrentVerticalResolution);
        var minDimension = Math.Min(controller.CurrentHorizontalResolution, controller.CurrentVerticalResolution);

        if (maxDimension >= 3840 || minDimension >= 2160)
        {
            return "4K+";
        }

        if (maxDimension >= 2560 || minDimension >= 1440)
        {
            return "QHD";
        }

        if (maxDimension >= 1920 || minDimension >= 1080)
        {
            return "Full HD";
        }

        return maxDimension >= 1280 || minDimension >= 720
            ? "HD"
            : "SD";
    }

    private string BuildMonitorModeSummary(HardwareInfoMonitor monitor)
    {
        var resolutionTier = GetDisplayTierLabel(monitor);
        return monitor.Active && monitor.CurrentRefreshRate > 0 && !string.Equals(resolutionTier, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? $"{resolutionTier} · {monitor.DisplayCurrentRefreshRate}"
            : resolutionTier;
    }

    private string GetDisplayTierLabel(HardwareInfoMonitor monitor)
    {
        if (!monitor.Active)
        {
            return "Inactive";
        }

        var horizontalResolution = monitor.CurrentHorizontalResolution;
        var verticalResolution = monitor.CurrentVerticalResolution;

        if ((horizontalResolution == 0 || verticalResolution == 0)
            && FindLinkedVideoController(monitor) is { } linkedController)
        {
            horizontalResolution = linkedController.CurrentHorizontalResolution;
            verticalResolution = linkedController.CurrentVerticalResolution;
        }

        if (horizontalResolution == 0 && verticalResolution == 0)
        {
            return "Unknown";
        }

        var maxDimension = Math.Max(horizontalResolution, verticalResolution);
        var minDimension = Math.Min(horizontalResolution, verticalResolution);

        if (maxDimension >= 3840 || minDimension >= 2160)
        {
            return "4K+";
        }

        if (maxDimension >= 2560 || minDimension >= 1440)
        {
            return "QHD";
        }

        if (maxDimension >= 1920 || minDimension >= 1080)
        {
            return "Full HD";
        }

        return maxDimension >= 1280 || minDimension >= 720
            ? "HD"
            : "SD";
    }

    private HardwareInfoVideoController? FindLinkedVideoController(HardwareInfoMonitor monitor)
    {
        return Snapshot?.Runtime.VideoControllers.FirstOrDefault(controller => IsMonitorLinkedToController(monitor, controller));
    }

    private int? FindLinkedVideoControllerCardIndex(HardwareInfoMonitor monitor)
    {
        for (var index = 0; index < _videoControllerCards.Count; index++)
        {
            if (IsMonitorLinkedToController(monitor, _videoControllerCards[index].Snapshot))
            {
                return index;
            }
        }

        return null;
    }

    private static bool IsMonitorLinkedToController(HardwareInfoMonitor monitor, HardwareInfoVideoController controller)
    {
        if (monitor.LinkedVideoControllerDisplayNames.Any())
        {
            var controllerCandidates = GetControllerLinkCandidates(controller);
            if (monitor.LinkedVideoControllerDisplayNames.Any(linkedDisplayName =>
                controllerCandidates.Any(candidate => string.Equals(candidate, linkedDisplayName, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        if (controller.LinkedMonitorDisplayNames.Any())
        {
            var monitorCandidates = GetMonitorLinkCandidates(monitor);
            if (controller.LinkedMonitorDisplayNames.Any(linkedMonitorName =>
                monitorCandidates.Any(candidate => string.Equals(candidate, linkedMonitorName, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetControllerLinkCandidates(HardwareInfoVideoController controller)
    {
        if (!string.IsNullOrWhiteSpace(controller.Name))
        {
            yield return controller.Name;
        }

        if (!string.IsNullOrWhiteSpace(controller.Caption))
        {
            yield return controller.Caption;
        }

        if (!string.IsNullOrWhiteSpace(controller.Description))
        {
            yield return controller.Description;
        }

        if (!string.IsNullOrWhiteSpace(controller.VideoProcessor))
        {
            yield return controller.VideoProcessor;
        }
    }

    private static IEnumerable<string> GetMonitorLinkCandidates(HardwareInfoMonitor monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.DisplayName))
        {
            yield return monitor.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(monitor.UserFriendlyName))
        {
            yield return monitor.UserFriendlyName;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Name))
        {
            yield return monitor.Name;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Caption))
        {
            yield return monitor.Caption;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Description))
        {
            yield return monitor.Description;
        }
    }

    private string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private string? GetSystemProfileProductName()
    {
        return FirstNonEmptyExcluding(
            SystemProfileModel,
            Snapshot?.Inventory.ComputerSystem?.Skunumber,
            Snapshot?.Inventory.ComputerSystem?.Name,
            Snapshot?.Inventory.ComputerSystem?.Caption,
            FrameworkStatus?.DeviceModel);
    }

    private string? FirstNonEmptyExcluding(string? excludedValue, params string?[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, excludedValue, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplySnapshotChanges<TSnapshot>(Dictionary<int, TSnapshot> target, IChangeSet<TSnapshot, int> set)
        where TSnapshot : notnull
    {
        foreach (Change<TSnapshot, int> change in set)
        {
            if (change.Reason == ChangeReason.Remove)
            {
                target.Remove(change.Key);
                continue;
            }

            target[change.Key] = change.Current;
        }
    }

    private string GetTemperatureStatus(TemperatureTelemetrySnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            return "Unavailable";
        }

        return snapshot.TemperatureState switch
        {
            FrameworkTemperatureState.NotPresent => "Not Present",
            FrameworkTemperatureState.NotPowered => "Not Powered",
            FrameworkTemperatureState.NotCalibrated => "Not Calibrated",
            FrameworkTemperatureState.Error => "Error",
            _ => "OK"
        };
    }

    private string GetFanStatus(FanTelemetrySnapshot? snapshot, FanStateSnapshot? state)
    {
        if (state is not null)
        {
            if (!state.IsAvailable)
            {
                return "Unavailable";
            }

            return state.FanState switch
            {
                FrameworkFanState.Ok => "OK",
                FrameworkFanState.Stalled => "Stalled",
                FrameworkFanState.NotPresent => "Not Present",
                _ => "Unknown"
            };
        }

        return snapshot?.IsAvailable == true
            ? "Checking"
            : "Unavailable";
    }

    private string GetBatteryStatus(BatteryTelemetrySnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            return "Unavailable";
        }

        if (snapshot.BatteryState == FrameworkBatteryState.NotPresent)
        {
            return "Not Present";
        }

        if (snapshot.BatteryState is FrameworkBatteryState batteryState
            && snapshot.PowerSourceState is FrameworkPowerSourceState powerSourceState
            && powerSourceState != FrameworkPowerSourceState.None)
        {
            return $"{FormatBatteryState(batteryState)} / {FormatPowerSourceState(powerSourceState)}";
        }

        if (snapshot.BatteryState is FrameworkBatteryState state)
        {
            return FormatBatteryState(state);
        }

        return snapshot.PowerSourceState is FrameworkPowerSourceState sourceState
            ? FormatPowerSourceState(sourceState)
            : "OK";
    }

    private string FormatBatteryState(FrameworkBatteryState state)
    {
        return state switch
        {
            FrameworkBatteryState.NotPresent => "Not Present",
            _ => state.ToString()
        };
    }

    private string FormatPowerSourceState(FrameworkPowerSourceState state)
    {
        return state switch
        {
            FrameworkPowerSourceState.None => "Unknown",
            FrameworkPowerSourceState.AcAndBattery => "AC and Battery",
            FrameworkPowerSourceState.AcOnly => "AC Only",
            FrameworkPowerSourceState.BatteryOnly => "Battery Only",
            _ => "Unknown"
        };
    }

    private DeviceCapabilitiesStatusTone GetStatusTone(string status)
    {
        if (status.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Stalled", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceCapabilitiesStatusTone.Error;
        }

        if (status.Contains("Not Present", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Not Powered", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Not Calibrated", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Checking", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceCapabilitiesStatusTone.Warning;
        }

        return DeviceCapabilitiesStatusTone.Success;
    }

    private async Task SynchronizeCards<TCard, TSource>(
        ObservableCollection<TCard> target,
        IReadOnlyList<TSource> source,
        Func<int, TSource, TCard> create,
        Action<TCard, int, TSource> update)
    {
        for (var index = 0; index < source.Count; index++)
        {
            var item = source[index];
            if (index < target.Count)
            {
                await _dispatcherQueue.EnqueueAsync(() => update(target[index], index, item));
                continue;
            }

            await _dispatcherQueue.EnqueueAsync(() => target.Add(create(index, item)));
        }

        while (target.Count > source.Count)
        {
            await _dispatcherQueue.EnqueueAsync(() => target.RemoveAt(target.Count - 1));
        }
    }

    public void Dispose()
    {
        OnboardStatusSection.Dispose();
        CpuSection.Dispose();
        StorageSection.Dispose();
        MemorySection.Dispose();
        PlatformFirmwareSection.Dispose();
        GraphicsSection.Dispose();
        NetworkSection.Dispose();
        SystemProfileSection.Dispose();
        CoolingSection.Dispose();
        _subscriptions.Dispose();
    }
}
