using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using FrameworkDotnet.Enums;
using LiveChartsCore.Defaults;
using Material.Icons;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

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
    private readonly IUnitFormattingService _unitFormattingService;
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
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _temperatureStatusItems = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _fanStatusItems = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _batteryStatusItems = [];
    private HistoricalRecord<HardwareInfoSnapshot>[] _cpuHistoryRecords = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(CpuCount),
        nameof(TotalCoreCount),
        nameof(TotalLogicalProcessorCount),
        nameof(GraphicsAdapterCount),
        nameof(MonitorCount),
        nameof(ActiveMonitorCount),
        nameof(StorageDriveCount),
        nameof(PrimaryDisplayName),
        nameof(PrimaryDisplayBadge),
        nameof(TotalStorageFreeBrush),
        nameof(TotalStorageUsagePercent),
        nameof(TotalStorageUsageBarBrush),
        nameof(PhysicalMemoryUsageBarBrush),
        nameof(NetworkAdapterCount),
        nameof(NetworkAdapterCountDisplay),
        nameof(ConnectedNetworkAdapterCount),
        nameof(ConnectedNetworkAdapterCountDisplay),
        nameof(ConnectedNetworkAdapterBrush),
        nameof(FastestLinkDisplay),
        nameof(FastestLinkBrush),
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
        nameof(PhysicalMemoryUsagePercent),
        nameof(PhysicalMemoryUsageTone),
        nameof(PhysicalMemoryUsageSuccessVisibility),
        nameof(PhysicalMemoryUsageWarningVisibility),
        nameof(PhysicalMemoryUsageErrorVisibility))]
    public partial HardwareInfoSnapshot? Snapshot { get; set; }

    // The unit-formatted aggregate displays are STORED properties; re-project them whenever the snapshot changes.
    partial void OnSnapshotChanged(HardwareInfoSnapshot? value) => RefreshUnitFormattedDisplays();

    public ReadOnlyObservableCollection<DeviceCapabilitiesCpuPackageCardModel> CpuPackageCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMemoryModuleCardModel> MemoryModuleCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesStorageDriveCardModel> StorageDriveCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesNetworkAdapterCardModel> NetworkAdapterCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel> VideoControllerCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesGraphicsCardGroupModel> GraphicsCardGroups { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel> MonitorCards { get; }

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

    // ----- Category rail (two-pane layout): Onboard · CPU · Memory · Storage · Graphics · Network · System profile -----

    public ObservableCollection<DeviceCapabilitiesCategoryRailItemModel> Categories { get; } =
    [
        new(0, "Onboard devices", MaterialIconKind.Devices),
        new(1, "CPU", MaterialIconKind.Chip),
        new(2, "Memory", MaterialIconKind.Memory),
        new(3, "Storage", MaterialIconKind.Harddisk),
        new(4, "Graphics", MaterialIconKind.ExpansionCard),
        new(5, "Network", MaterialIconKind.Lan),
        new(6, "System profile", MaterialIconKind.InformationOutline),
    ];

    /// <summary>Selected rail entry; the page's code-behind mirrors it into the category navigation sub-region.</summary>
    [ObservableProperty]
    public partial int SelectedCategoryIndex { get; set; }

    [RelayCommand]
    private void SelectCategory(DeviceCapabilitiesCategoryRailItemModel category)
    {
        foreach (var entry in Categories)
        {
            entry.IsSelected = entry.Index == category.Index;
        }

        SelectedCategoryIndex = category.Index;
    }

    /// <summary>Refreshes the rail count badges from the live collections; must run on the dispatcher.</summary>
    private void RefreshCategoryCounts()
    {
        Categories[0].Count = _temperatureStatusItems.Count + _fanStatusItems.Count + _batteryStatusItems.Count;
        Categories[1].Count = CpuCount;
        Categories[2].Count = MemoryModuleCount;
        Categories[3].Count = StorageDriveCount;
        Categories[4].Count = GraphicsAdapterCount;
        Categories[5].Count = NetworkAdapterCount;
    }

    public int CpuCount => Snapshot?.Runtime.Cpus.Length ?? 0;

    public int TotalCoreCount => Snapshot?.Runtime.Cpus.Sum(cpu => cpu.Cores) ?? 0;

    public int TotalLogicalProcessorCount => Snapshot?.Runtime.Cpus.Sum(cpu => cpu.LogicalProcessors) ?? 0;

    [ObservableProperty]
    public partial string AverageClockSpeed { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string AverageMaxClockSpeed { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string AverageCpuUsageDisplay { get; private set; } = "Unknown";

    public int GraphicsAdapterCount => Snapshot?.Runtime.VideoControllers.Length ?? 0;

    public int MonitorCount => Snapshot?.Runtime.Monitors.Length is > 0
        ? Snapshot.Runtime.Monitors.Length
        : Snapshot?.Runtime.VideoControllers.Count(HasActiveDisplay) ?? 0;

    public int ActiveMonitorCount => Snapshot?.Runtime.Monitors.Length is > 0
        ? Snapshot.Runtime.Monitors.Count(monitor => monitor.Active)
        : Snapshot?.Runtime.VideoControllers.Count(HasActiveDisplay) ?? 0;

    public int StorageDriveCount => Snapshot?.Inventory.Drives.Length ?? 0;

    /// <summary>Primary display name + tier badge for the Graphics summary (mockup "Primary display" tile).</summary>
    public string PrimaryDisplayName
    {
        get
        {
            var monitors = Snapshot?.Runtime.Monitors ?? [];
            var primary = monitors.FirstOrDefault(monitor => monitor.Active) ?? monitors.FirstOrDefault();
            return primary is null ? "Unknown" : primary.DisplayName;
        }
    }

    public string PrimaryDisplayBadge
    {
        get
        {
            var monitors = Snapshot?.Runtime.Monitors ?? [];
            var primary = monitors.FirstOrDefault(monitor => monitor.Active) ?? monitors.FirstOrDefault();
            return primary is null ? string.Empty : GetDisplayTierLabel(primary);
        }
    }

    [ObservableProperty]
    public partial string TotalStorageCapacity { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string TotalStorageUsedSpace { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string TotalStorageFreeSpace { get; private set; } = "Unknown";

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

    [ObservableProperty]
    public partial string TotalStorageUsageSummary { get; private set; } = "Unknown";

    /// <summary>Mockup state colour for the aggregate Free value (red nearly full, amber low).</summary>
    public Brush TotalStorageFreeBrush => DeviceCapabilitiesStorageDriveCardModel.FreePercentBrush(
        Snapshot?.Inventory.Drives.Length > 0 ? 100d - TotalStorageUsagePercent : null);

    public Brush TotalStorageUsageBarBrush => UsageBarBrush(TotalStorageUsagePercent);

    public Brush PhysicalMemoryUsageBarBrush => UsageBarBrush(PhysicalMemoryUsagePercent);

    /// <summary>Usage-bar fill per the mockup: green when comfortable, amber when high, red when nearly full.</summary>
    private static Brush UsageBarBrush(double percent) => percent switch
    {
        < 75d => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        < 90d => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        _ => AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor),
    };

    public int NetworkAdapterCount => Snapshot?.Inventory.NetworkAdapters.Length ?? 0;

    public string NetworkAdapterCountDisplay => NetworkAdapterCount.ToString();

    public string ConnectedNetworkAdapterCountDisplay => ConnectedNetworkAdapterCount.ToString();

    public Brush ConnectedNetworkAdapterBrush => ConnectedNetworkAdapterCount > 0
        ? AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusWarningColor);

    /// <summary>Fastest reported link speed across adapters ("1.2 Gbps"), for the mockup's summary tile.</summary>
    public string FastestLinkDisplay
    {
        get
        {
            var fastest = Snapshot?.Inventory.NetworkAdapters
                .Where(adapter => adapter.HasKnownSpeed
                    && !DeviceCapabilitiesNetworkAdapterCardModel.IsTunnelAdapter(adapter))
                .Select(adapter => (double)adapter.Speed)
                .DefaultIfEmpty(0d)
                .Max() ?? 0d;

            return fastest > 0d ? _unitFormattingService.FormatBitRateBitsPerSecond(fastest) : "Unknown";
        }
    }

    public Brush FastestLinkBrush => FastestLinkDisplay == "Unknown"
        ? AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusWarningColor)
        : AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor);

    /// <summary>Coarse connectivity flag for the Network category's Internet tile; refreshed with each
    /// HardwareInfo snapshot (HardwareInfo itself carries no internet flag).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InternetStatusDisplay))]
    [NotifyPropertyChangedFor(nameof(InternetStatusBrush))]
    public partial bool IsInternetAvailable { get; set; }

    public string InternetStatusDisplay => IsInternetAvailable ? "Connected" : "Offline";

    public Brush InternetStatusBrush => IsInternetAvailable
        ? AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor);

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

    [ObservableProperty]
    public partial string MemoryTotalCapacity { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string TotalPhysicalMemory { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string AvailablePhysicalMemory { get; private set; } = "Unknown";

    public double PhysicalMemoryUsagePercent => Snapshot?.Runtime.MemoryStatus is { TotalPhysical: > 0 } memoryStatus
        ? Math.Clamp((memoryStatus.TotalPhysical - Math.Min(memoryStatus.AvailablePhysical, memoryStatus.TotalPhysical)) * 100d / memoryStatus.TotalPhysical, 0d, 100d)
        : 0d;

    [ObservableProperty]
    public partial string PhysicalMemoryUsageDisplay { get; private set; } = "Unknown";

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

    [ObservableProperty]
    public partial string TotalPageFileMemory { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string AvailablePageFileMemory { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string TotalVirtualMemory { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string AvailableVirtualMemory { get; private set; } = "Unknown";

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

    // Reassigns every unit-formatted aggregate display from the current snapshot + unit preference. Called
    // when the snapshot changes (OnSnapshotChanged) and when the unit preference changes; the stored-property
    // setters raise PropertyChanged only for values that actually changed. On the UI thread (all streams
    // ObserveOn the UI synchronization context).
    private void RefreshUnitFormattedDisplays()
    {
        AverageClockSpeed = Snapshot?.Runtime.Cpus.Length > 0
            ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.Runtime.Cpus.Average(cpu => cpu.CurrentClockSpeedMHz))
            : "Unknown";
        AverageMaxClockSpeed = Snapshot?.Runtime.Cpus.Length > 0
            ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.Runtime.Cpus.Average(cpu => cpu.MaxClockSpeedMHz))
            : "Unknown";

        var averageUsage = GetAverageCpuUsagePercent(Snapshot);
        AverageCpuUsageDisplay = averageUsage is double usageValue
            ? _unitFormattingService.FormatRatio(Math.Clamp(usageValue, 0d, 100d), decimals: 1)
            : "Unknown";

        TotalStorageCapacity = Snapshot?.Inventory.Drives.Length > 0
            ? FormatBytes(Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.Size))
            : "Unknown";
        TotalStorageUsedSpace = Snapshot?.Inventory.Drives.Length > 0
            ? FormatBytes(Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.UsedSpace))
            : "Unknown";
        TotalStorageFreeSpace = Snapshot?.Inventory.Drives.Length > 0
            ? FormatBytes(Snapshot.Inventory.Drives.Aggregate(0UL, (total, drive) => total + drive.ClampedFreeSpace))
            : "Unknown";
        TotalStorageUsageSummary = Snapshot?.Inventory.Drives.Length > 0
            ? $"{TotalStorageUsedSpace} used / {TotalStorageFreeSpace} free"
            : "Unknown";

        MemoryTotalCapacity = Snapshot?.Inventory.MemoryModules.Length > 0
            ? FormatBytes((ulong)Snapshot.Inventory.MemoryModules.Sum(module => (long)module.CapacityBytes))
            : "Unknown";
        TotalPhysicalMemory = Snapshot?.Runtime.MemoryStatus is null
            ? "Unknown"
            : FormatBytes(Snapshot.Runtime.MemoryStatus.TotalPhysical);
        AvailablePhysicalMemory = Snapshot?.Runtime.MemoryStatus is null
            ? "Unknown"
            : FormatBytes(Snapshot.Runtime.MemoryStatus.AvailablePhysical);
        PhysicalMemoryUsageDisplay = Snapshot?.Runtime.MemoryStatus is { TotalPhysical: > 0 } physicalMemory
            ? $"{_unitFormattingService.FormatRatio(PhysicalMemoryUsagePercent, decimals: 0)} used ({FormatBytes(physicalMemory.TotalPhysical - Math.Min(physicalMemory.AvailablePhysical, physicalMemory.TotalPhysical))} / {FormatBytes(physicalMemory.TotalPhysical)})"
            : "Unknown";
        TotalPageFileMemory = Snapshot?.Runtime.MemoryStatus is null
            ? "Unknown"
            : FormatBytes(Snapshot.Runtime.MemoryStatus.TotalPageFile);
        AvailablePageFileMemory = Snapshot?.Runtime.MemoryStatus is null
            ? "Unknown"
            : FormatBytes(Snapshot.Runtime.MemoryStatus.AvailablePageFile);
        TotalVirtualMemory = Snapshot?.Runtime.MemoryStatus is null
            ? "Unknown"
            : FormatBytes(Snapshot.Runtime.MemoryStatus.TotalVirtual);
        AvailableVirtualMemory = Snapshot?.Runtime.MemoryStatus is null
            ? "Unknown"
            : FormatBytes(Snapshot.Runtime.MemoryStatus.AvailableVirtual);
    }

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
        IUserUnitPreferencesClient userUnitPreferencesClient,
        IUnitFormattingService unitFormattingService,
        SynchronizationContext synchronizationContext,
        DispatcherQueue dispatcherQueue,
        DeviceCapabilitiesAccessor accessor)
    {
        // Publish this instance for the navigation-resolved category body VMs (see DeviceCapabilitiesAccessor).
        accessor.Current = this;

        _hardwareInfoClient = hardwareInfoClient;
        _fanStateClient = fanStateClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _batteryTelemetryClient = batteryTelemetryClient;
        _frameworkStatusClient = frameworkStatusClient;
        _unitFormattingService = unitFormattingService;
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
        TemperatureStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_temperatureStatusItems);
        FanStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_fanStatusItems);
        BatteryStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_batteryStatusItems);

        Categories[0].IsSelected = true;

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

        userUnitPreferencesClient
            .WatchPreferences()
            .ObserveOn(_synchronizationContext)
            .Select(_ => Observable.FromAsync(RefreshUnitFormattingAsync))
            .Concat()
            .Subscribe(_ => { })
            .DisposeWith(_subscriptions);
    }

    private async Task UpdateSnapshotAsync(HardwareInfoSnapshot snapshot)
    {
        var internetAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        await _dispatcherQueue.EnqueueAsync(() =>
        {
            Snapshot = snapshot;
            IsInternetAvailable = internetAvailable;
        });
        await RefreshSnapshotCardsAsync();
        await RefreshCpuVisualsAsync();
        await _dispatcherQueue.EnqueueAsync(RefreshCategoryCounts);
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
        await _dispatcherQueue.EnqueueAsync(RefreshCategoryCounts);
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
        await _dispatcherQueue.EnqueueAsync(RefreshCategoryCounts);
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
        await _dispatcherQueue.EnqueueAsync(RefreshCategoryCounts);
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
                        model = new FrameworkLaptop12FanAdvancedInfoCardModel(_unitFormattingService);
                        FanAdvancedInfo = model;
                    }

                    model.UpdateFrom(details);
                    return;
                }
                case FrameworkLaptop13CoolingDetails details:
                {
                    if (FanAdvancedInfo is not FrameworkLaptop13FanAdvancedInfoCardModel model)
                    {
                        model = new FrameworkLaptop13FanAdvancedInfoCardModel(_unitFormattingService);
                        FanAdvancedInfo = model;
                    }

                    model.UpdateFrom(details);
                    return;
                }
                case FrameworkLaptop16CoolingDetails details:
                {
                    if (FanAdvancedInfo is not FrameworkLaptop16FanAdvancedInfoCardModel model)
                    {
                        model = new FrameworkLaptop16FanAdvancedInfoCardModel(_unitFormattingService);
                        FanAdvancedInfo = model;
                    }

                    model.UpdateFrom(details);
                    return;
                }
                case FrameworkDesktopCoolingDetails details:
                {
                    if (FanAdvancedInfo is not FrameworkDesktopFanAdvancedInfoCardModel model)
                    {
                        model = new FrameworkDesktopFanAdvancedInfoCardModel(_unitFormattingService);
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
    }

    private DateTimePoint[] BuildCpuClockHistory()
    {
        List<DateTimePoint> points = [
            .. _cpuHistoryRecords
                .Where(record => record.Value.IsAvailable && record.Value.Runtime.Cpus.Length > 0)
                .Select(record => new DateTimePoint(
                    record.ObservedAt.LocalDateTime,
                    _unitFormattingService.ConvertClockFrequencyMegahertz(record.Value.Runtime.Cpus.Average(cpu => cpu.CurrentClockSpeedMHz))))
        ];

        if (Snapshot?.IsAvailable == true && Snapshot.Runtime.Cpus.Length > 0)
        {
            var observedAt = Snapshot.ObservedAt.LocalDateTime;
            if (points.Count == 0 || observedAt > points[^1].DateTime)
            {
                points.Add(new DateTimePoint(
                    observedAt,
                    _unitFormattingService.ConvertClockFrequencyMegahertz(Snapshot.Runtime.Cpus.Average(cpu => cpu.CurrentClockSpeedMHz))));
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
                    ConvertRatioValue(GetAverageCpuUsagePercent(record.Value))))
        ];

        if (Snapshot?.IsAvailable == true && Snapshot.Runtime.Cpus.Length > 0)
        {
            var observedAt = Snapshot.ObservedAt.LocalDateTime;
            if (points.Count == 0 || observedAt > points[^1].DateTime)
            {
                points.Add(new DateTimePoint(observedAt, ConvertRatioValue(GetAverageCpuUsagePercent(Snapshot))));
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
            PresentationDefaults.StandardTelemetryHistorySeparatorStep);

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
            PresentationDefaults.StandardTelemetryHistorySeparatorStep);

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

    private void AppendCpuCoreHistoryPoints(
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
                    _unitFormattingService.ConvertRatio(Math.Clamp(cpu.CpuCores[coreIndex].PercentProcessorTime, 0d, 100d))));
            }
        }
    }

    private void AppendCpuPackageUsageHistoryPoints(
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
                usageValue is null ? null : _unitFormattingService.ConvertRatio(Math.Clamp(usageValue.Value, 0d, 100d))));
        }
    }

    private void AppendCpuPackageClockHistoryPoints(
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
                _unitFormattingService.ConvertClockFrequencyMegahertz(snapshot.Runtime.Cpus[packageIndex].CurrentClockSpeedMHz)));
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
            (index, cpu) => new DeviceCapabilitiesCpuPackageCardModel(index, cpu, _unitFormattingService),
            (card, index, cpu) =>
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
            (_, memoryModule) => new DeviceCapabilitiesMemoryModuleCardModel(memoryModule, _unitFormattingService),
            static (card, _, memoryModule) => card.Snapshot = memoryModule);
    }

    private Task SynchronizeStorageDriveCardsAsync(IReadOnlyList<HardwareInfoDrive> drives)
    {
        return SynchronizeCards(
            _storageDriveCards,
            drives,
            (_, drive) => new DeviceCapabilitiesStorageDriveCardModel(drive, _unitFormattingService),
            static (card, _, drive) => card.Snapshot = drive);
    }

    private Task SynchronizeNetworkAdapterCardsAsync(IReadOnlyList<HardwareInfoNetworkAdapter> networkAdapters)
    {
        return SynchronizeCards(
            _networkAdapterCards,
            networkAdapters,
            (_, networkAdapter) => new DeviceCapabilitiesNetworkAdapterCardModel(networkAdapter, _unitFormattingService),
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
        var enrichedMonitors = monitors.Select(EnrichMonitorWithLinkedControllerMode).ToArray();

        return SynchronizeCards(
            _monitorCards,
            enrichedMonitors,
            (index, monitor) => new DeviceCapabilitiesMonitorCardModel(index, monitor, _unitFormattingService),
            static (card, index, monitor) =>
            {
                card.Index = index;
                card.Snapshot = monitor;
            });
    }

    /// <summary>WMI monitors often report no display mode of their own; borrow it from the linked graphics
    /// adapter (same fallback the display tier labels use) so the monitor detail shows the real resolution.</summary>
    private HardwareInfoMonitor EnrichMonitorWithLinkedControllerMode(HardwareInfoMonitor monitor)
    {
        if (!monitor.Active
            || (monitor.CurrentHorizontalResolution > 0 && monitor.CurrentVerticalResolution > 0)
            || FindLinkedVideoController(monitor) is not { } linkedController)
        {
            return monitor;
        }

        return monitor with
        {
            CurrentHorizontalResolution = linkedController.CurrentHorizontalResolution,
            CurrentVerticalResolution = linkedController.CurrentVerticalResolution,
            CurrentRefreshRate = monitor.CurrentRefreshRate > 0 ? monitor.CurrentRefreshRate : linkedController.CurrentRefreshRate,
        };
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

        List<(DeviceCapabilitiesVideoControllerCardModel? VideoController, bool IsUnknownGraphicsCard, int AdapterIndex, IReadOnlyList<DeviceCapabilitiesMonitorCardModel> MonitorCards)> groups = [];

        for (var controllerIndex = 0; controllerIndex < _videoControllerCards.Count; controllerIndex++)
        {
            var controllerCard = _videoControllerCards[controllerIndex];
            groups.Add((controllerCard, false, controllerIndex, monitorCardsByControllerIndex[controllerIndex]));
        }

        if (unknownMonitorCards.Count > 0)
        {
            groups.Add((null, true, -1, unknownMonitorCards));
        }

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            if (index < _graphicsCardGroups.Count)
            {
                await _dispatcherQueue.EnqueueAsync(() =>
                {
                    _graphicsCardGroups[index].IsUnknownGraphicsCard = group.IsUnknownGraphicsCard;
                    _graphicsCardGroups[index].AdapterIndex = group.AdapterIndex;
                    _graphicsCardGroups[index].VideoController = group.VideoController;
                    _graphicsCardGroups[index].SynchronizeMonitorCards(group.MonitorCards);
                });

                continue;
            }

            await _dispatcherQueue.EnqueueAsync(() =>
            {
                var graphicsCardGroup = new DeviceCapabilitiesGraphicsCardGroupModel(group.IsUnknownGraphicsCard, group.AdapterIndex, group.VideoController);
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
        return _unitFormattingService.FormatInformationBytes(bytes);
    }

    private async Task RefreshUnitFormattingAsync()
    {
        await _dispatcherQueue.EnqueueAsync(() =>
        {
            foreach (var cpuPackageCard in _cpuPackageCards)
            {
                cpuPackageCard.RefreshUnitFormatting();
            }

            foreach (var memoryModuleCard in _memoryModuleCards)
            {
                memoryModuleCard.RefreshUnitFormatting();
            }

            foreach (var storageDriveCard in _storageDriveCards)
            {
                storageDriveCard.RefreshUnitFormatting();
            }

            foreach (var networkAdapterCard in _networkAdapterCards)
            {
                networkAdapterCard.RefreshUnitFormatting();
            }

            foreach (var monitorCard in _monitorCards)
            {
                monitorCard.RefreshUnitFormatting();
            }

            FanAdvancedInfo?.RefreshUnitFormatting();

            RefreshUnitFormattedDisplays();
        });

        await RefreshCpuVisualsAsync();
    }

    private Task RefreshTemperatureStatusItemsAsync()
    {
        var sensors = _temperatureSnapshots.Values
            .OrderBy(snapshot => snapshot.SensorIndex)
            .ToArray();

        return SynchronizeCards(
            _temperatureStatusItems,
            sensors,
            (_, snapshot) =>
            {
                var card = new DeviceCapabilitiesRuntimeStatusItemModel(
                    $"Sensor {snapshot.SensorIndex}",
                    GetTemperatureStatus(snapshot),
                    GetStatusTone(GetTemperatureStatus(snapshot)));
                ApplyTemperatureTile(card, snapshot);
                return card;
            },
            (card, _, snapshot) =>
            {
                card.Name = $"Sensor {snapshot.SensorIndex}";
                card.Status = GetTemperatureStatus(snapshot);
                card.StatusTone = GetStatusTone(card.Status);
                ApplyTemperatureTile(card, snapshot);
            });
    }

    private void ApplyTemperatureTile(DeviceCapabilitiesRuntimeStatusItemModel card, TemperatureTelemetrySnapshot snapshot)
    {
        card.IconKind = MaterialIconKind.Thermometer;
        card.ValueDisplay = _unitFormattingService.FormatTemperature(snapshot.IsAvailable ? snapshot.TemperatureCelsius : null);
        card.ValueBrush = GetTemperatureValueBrush(snapshot.IsAvailable ? snapshot.TemperatureCelsius : null);
        card.Location = FrameworkSensorNameDisplay.ToLocation(snapshot.SensorName);
    }

    private static Brush GetTemperatureValueBrush(double? celsius) => celsius switch
    {
        null => AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.StatusErrorColor),
        < 45d => AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusSuccessColor),
        < 65d => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
        < 85d => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        _ => AppThemeBrushes.Get("StatusErrorBrush", AppThemeBrushes.StatusErrorColor),
    };

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
            (_, fan) =>
            {
                var card = new DeviceCapabilitiesRuntimeStatusItemModel(
                    GetFanTileTitle(fan.Index, fan.Snapshot),
                    GetFanStatus(fan.Snapshot, fan.State),
                    GetStatusTone(GetFanStatus(fan.Snapshot, fan.State)));
                ApplyFanTile(card, fan.Snapshot);
                return card;
            },
            (card, _, fan) =>
            {
                card.Name = GetFanTileTitle(fan.Index, fan.Snapshot);
                card.Status = GetFanStatus(fan.Snapshot, fan.State);
                card.StatusTone = GetStatusTone(card.Status);
                ApplyFanTile(card, fan.Snapshot);
            });
    }

    // The tile leads with the cooling function ("CPU fan"); the physical location ("Left fan") becomes the
    // sub-line. Fans without an identified function keep their location (or index) label as the title.
    private static string GetFanTileTitle(int fanIndex, FanTelemetrySnapshot? snapshot)
        => FrameworkFanNameDisplay.ToFunction(snapshot?.FanName)
            ?? snapshot?.DisplayName
            ?? $"Fan {fanIndex}";

    private void ApplyFanTile(DeviceCapabilitiesRuntimeStatusItemModel card, FanTelemetrySnapshot? snapshot)
    {
        card.IconKind = MaterialIconKind.Fan;
        card.ValueDisplay = _unitFormattingService.FormatFanSpeed(snapshot?.IsAvailable == true ? snapshot.SpeedRpm : null);
        card.ValueBrush = AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusSuccessColor);
        card.Location = FrameworkFanNameDisplay.ToFunction(snapshot?.FanName) is null ? null : snapshot?.DisplayName;
    }

    private Task RefreshBatteryStatusItemsAsync()
    {
        var batteries = _batterySnapshots.Values
            .OrderBy(snapshot => snapshot.BatteryIndex)
            .ToArray();

        return SynchronizeCards(
            _batteryStatusItems,
            batteries,
            (_, snapshot) =>
            {
                var card = new DeviceCapabilitiesRuntimeStatusItemModel(
                    $"Battery {snapshot.BatteryIndex}",
                    string.Empty,
                    DeviceCapabilitiesStatusTone.Success);
                ApplyBatteryTile(card, snapshot);
                return card;
            },
            (card, _, snapshot) =>
            {
                card.Name = $"Battery {snapshot.BatteryIndex}";
                ApplyBatteryTile(card, snapshot);
            });
    }

    private void ApplyBatteryTile(DeviceCapabilitiesRuntimeStatusItemModel card, BatteryTelemetrySnapshot snapshot)
    {
        card.IconKind = MaterialIconKind.BatteryHigh;
        card.ValueDisplay = _unitFormattingService.FormatRatio(snapshot.IsAvailable ? snapshot.ChargePercent : null, decimals: 0);
        card.ValueBrush = AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor);
        // The mockup battery tile leads with the state line and detail rows instead of a location.
        card.Location = null;

        if (!snapshot.IsAvailable)
        {
            card.Status = "Unavailable";
            card.StatusTone = DeviceCapabilitiesStatusTone.Error;
            card.StatusIconOverride = null;
            card.DetailLines.Clear();
            return;
        }

        var source = snapshot.PowerSourceState switch
        {
            FrameworkPowerSourceState.AcAndBattery => "AC + battery",
            FrameworkPowerSourceState.AcOnly => "AC only",
            FrameworkPowerSourceState.BatteryOnly => "Battery only",
            _ => null,
        };

        // No "Charging" word — the charging glyph + green tone already say it, so the source alone reads cleaner.
        var stateWord = snapshot.BatteryState switch
        {
            FrameworkBatteryState.Charging or FrameworkBatteryState.ChargingAndDischarging => null,
            FrameworkBatteryState.Discharging => "Discharging",
            FrameworkBatteryState.Critical => "Critical",
            FrameworkBatteryState.NotPresent => "Not present",
            _ => "Idle",
        };

        card.Status = (stateWord, source) switch
        {
            (null, not null) => source,
            (null, null) => "Charging",
            (not null, null) => stateWord,
            _ => $"{stateWord} · {source}",
        };
        card.StatusTone = snapshot.BatteryState switch
        {
            FrameworkBatteryState.Discharging => DeviceCapabilitiesStatusTone.Warning,
            FrameworkBatteryState.Critical or FrameworkBatteryState.NotPresent => DeviceCapabilitiesStatusTone.Error,
            _ => DeviceCapabilitiesStatusTone.Success,
        };
        card.StatusIconOverride = snapshot.BatteryState switch
        {
            FrameworkBatteryState.Discharging => MaterialIconKind.BatteryMinus,
            FrameworkBatteryState.Charging or FrameworkBatteryState.ChargingAndDischarging => MaterialIconKind.BatteryCharging,
            _ => null,
        };

        card.DetailLines.Clear();

        if (snapshot is { LastFullChargeCapacityAmpereHours: > 0 and double lastFull, DesignCapacityAmpereHours: > 0 and double design })
        {
            card.DetailLines.Add(new DeviceCapabilitiesTileLineModel(
                MaterialIconKind.HeartPulse,
                $"{Math.Min(lastFull / design * 100d, 100d):0.0}% health",
                AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor)));
        }

        if (snapshot.CycleCount is uint cycles)
        {
            card.DetailLines.Add(new DeviceCapabilitiesTileLineModel(
                MaterialIconKind.Counter,
                $"{cycles:N0} cycles",
                AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.StatusWarningColor)));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.BatteryType))
        {
            var chemistry = snapshot.BatteryType.Trim().ToUpperInvariant() switch
            {
                "LION" or "LI-ION" or "LIION" => "Li-ion",
                "LIP" or "LIPO" => "Li-po",
                _ => snapshot.BatteryType,
            };
            card.DetailLines.Add(new DeviceCapabilitiesTileLineModel(
                MaterialIconKind.Flask,
                chemistry,
                AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.StatusWarningColor)));
        }
    }

    private bool HasActiveDisplay(HardwareInfoVideoController controller)
    {
        return controller.CurrentHorizontalResolution > 0 && controller.CurrentVerticalResolution > 0;
    }

    private double? ConvertRatioValue(double? value)
    {
        return value is double currentValue
            ? _unitFormattingService.ConvertRatio(currentValue)
            : null;
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

        // 2,560 × 1,600 (16:10 panels, e.g. the Framework 16 display) reads as WQXGA rather than generic QHD.
        if (maxDimension >= 2560 && minDimension >= 1600)
        {
            return "WQXGA";
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
