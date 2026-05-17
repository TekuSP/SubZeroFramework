using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using FrameworkDotnet.Enums;
using LiveChartsCore.Defaults;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

public partial class DeviceCapabilitiesModel : ObservableObject, IDisposable
{
    private readonly IHardwareInfoClient _hardwareInfoClient;
    private readonly IFanStateClient _fanStateClient;
    private readonly IFanTelemetryClient _fanTelemetryClient;
    private readonly ITemperatureTelemetryClient _temperatureTelemetryClient;
    private readonly IBatteryTelemetryClient _batteryTelemetryClient;
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly Dictionary<int, TemperatureTelemetrySnapshot> _temperatureSnapshots = [];
    private readonly Dictionary<int, FanTelemetrySnapshot> _fanSnapshots = [];
    private readonly Dictionary<int, FanStateSnapshot> _fanStateSnapshots = [];
    private readonly Dictionary<int, BatteryTelemetrySnapshot> _batterySnapshots = [];
    private readonly ObservableCollection<DeviceCapabilitiesCpuPackageCardModel> _cpuPackageCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMemoryModuleCardModel> _memoryModuleCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesStorageDriveCardModel> _storageDriveCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesNetworkAdapterCardModel> _networkAdapterCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesVideoControllerCardModel> _videoControllerCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMonitorCardModel> _monitorCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMonitorResolutionCard> _monitorResolutionCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _temperatureStatusItems = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _fanStatusItems = [];
    private readonly ObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> _batteryStatusItems = [];
    private static readonly TimeSpan CpuClockHistoryWindow = TimeSpan.FromHours(1);
    private HistoricalRecord<HardwareInfoSnapshot>[] _cpuHistoryRecords = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(CpuCount),
        nameof(TotalCoreCount),
        nameof(TotalLogicalProcessorCount),
        nameof(AverageClockSpeed),
        nameof(AverageMaxClockSpeed),
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
        nameof(PhysicalMemoryUsageBrush),
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

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel> MonitorCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorResolutionCard> MonitorResolutionCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> TemperatureStatusItems { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> FanStatusItems { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> BatteryStatusItems { get; }

    public int CpuCount => Snapshot?.Runtime.Cpus.Length ?? 0;

    public int TotalCoreCount => Snapshot?.Runtime.Cpus.Sum(cpu => cpu.Cores) ?? 0;

    public int TotalLogicalProcessorCount => Snapshot?.Runtime.Cpus.Sum(cpu => cpu.LogicalProcessors) ?? 0;

    public string AverageClockSpeed => Snapshot?.Runtime.Cpus.Length > 0
        ? $"{Snapshot.Runtime.Cpus.Average(cpu => cpu.CurrentClockSpeedMHz):0} MHz"
        : "Unknown";

    public string AverageMaxClockSpeed => Snapshot?.Runtime.Cpus.Length > 0
        ? $"{Snapshot.Runtime.Cpus.Average(cpu => cpu.MaxClockSpeedMHz):0} MHz"
        : "Unknown";

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

    public Brush PhysicalMemoryUsageBrush => PhysicalMemoryUsagePercent switch
    {
        >= 90d => GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38)),
        >= 75d => GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78)),
        _ => GetBrush("StatusSuccessBrush", ColorHelper.FromArgb(255, 108, 203, 95)),
    };

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

    public DeviceCapabilitiesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IHardwareInfoClient hardwareInfoClient,
        IFanStateClient fanStateClient,
        IFanTelemetryClient fanTelemetryClient,
        ITemperatureTelemetryClient temperatureTelemetryClient,
        IBatteryTelemetryClient batteryTelemetryClient,
        IFrameworkStatusClient frameworkStatusClient,
        SynchronizationContext synchronizationContext)
    {
        _hardwareInfoClient = hardwareInfoClient;
        _fanStateClient = fanStateClient;
        _fanTelemetryClient = fanTelemetryClient;
        _temperatureTelemetryClient = temperatureTelemetryClient;
        _batteryTelemetryClient = batteryTelemetryClient;
        _frameworkStatusClient = frameworkStatusClient;
        _synchronizationContext = synchronizationContext;
        CpuPackageCards = new ReadOnlyObservableCollection<DeviceCapabilitiesCpuPackageCardModel>(_cpuPackageCards);
        MemoryModuleCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMemoryModuleCardModel>(_memoryModuleCards);
        StorageDriveCards = new ReadOnlyObservableCollection<DeviceCapabilitiesStorageDriveCardModel>(_storageDriveCards);
        NetworkAdapterCards = new ReadOnlyObservableCollection<DeviceCapabilitiesNetworkAdapterCardModel>(_networkAdapterCards);
        VideoControllerCards = new ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel>(_videoControllerCards);
        MonitorCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel>(_monitorCards);
        MonitorResolutionCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMonitorResolutionCard>(_monitorResolutionCards);
        TemperatureStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_temperatureStatusItems);
        FanStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_fanStatusItems);
        BatteryStatusItems = new ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel>(_batteryStatusItems);

        _hardwareInfoClient
            .WatchHardwareInfo()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateSnapshot)
            .DisposeWith(_subscriptions);

        _hardwareInfoClient
            .WatchHardwareInfoHistory(CpuClockHistoryWindow)
            .ObserveOn(_synchronizationContext)
            .ToCollection()
            .Subscribe(UpdateCpuClockHistory)
            .DisposeWith(_subscriptions);

        _frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateFrameworkStatus)
            .DisposeWith(_subscriptions);

        _temperatureTelemetryClient
            .WatchTemperatures()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyTemperatureChanges)
            .DisposeWith(_subscriptions);

        _fanTelemetryClient
            .WatchFans()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyFanTelemetryChanges)
            .DisposeWith(_subscriptions);

        _fanStateClient
            .WatchFanStates()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyFanStateChanges)
            .DisposeWith(_subscriptions);

        _batteryTelemetryClient
            .WatchBatteries()
            .ObserveOn(_synchronizationContext)
            .Subscribe(ApplyBatteryChanges)
            .DisposeWith(_subscriptions);
    }

    private void UpdateSnapshot(HardwareInfoSnapshot snapshot)
    {
        Snapshot = snapshot;
        RefreshSnapshotCards();
        RefreshCpuVisuals();
    }

    private void UpdateCpuClockHistory(IReadOnlyCollection<HistoricalRecord<HardwareInfoSnapshot>> history)
    {
        _cpuHistoryRecords = [
            .. history
                .OrderBy(record => record.ObservedAt)
                .ThenBy(record => record.SampleId)
        ];

        RefreshCpuVisuals();
    }

    private void UpdateFrameworkStatus(FrameworkSystemStatus status) => FrameworkStatus = status;

    private void ApplyTemperatureChanges(IChangeSet<TemperatureTelemetrySnapshot, int> set)
    {
        ApplySnapshotChanges(_temperatureSnapshots, set);
        RefreshTemperatureStatusItems();
    }

    private void ApplyFanTelemetryChanges(IChangeSet<FanTelemetrySnapshot, int> set)
    {
        ApplySnapshotChanges(_fanSnapshots, set);
        RefreshFanStatusItems();
    }

    private void ApplyFanStateChanges(IChangeSet<FanStateSnapshot, int> set)
    {
        ApplySnapshotChanges(_fanStateSnapshots, set);
        RefreshFanStatusItems();
    }

    private void ApplyBatteryChanges(IChangeSet<BatteryTelemetrySnapshot, int> set)
    {
        ApplySnapshotChanges(_batterySnapshots, set);
        RefreshBatteryStatusItems();
    }

    private void RefreshCpuVisuals()
    {
        CpuClockHistory = BuildCpuClockHistory();
        UpdateCpuClockHistoryAxis();
    }

    private void RefreshSnapshotCards()
    {
        SynchronizeCpuPackageCards(Snapshot?.Runtime.Cpus ?? []);
        SynchronizeMemoryModuleCards(Snapshot?.Inventory.MemoryModules ?? []);
        SynchronizeStorageDriveCards(Snapshot?.Inventory.Drives ?? []);
        SynchronizeNetworkAdapterCards(Snapshot?.Inventory.NetworkAdapters ?? []);
        SynchronizeVideoControllerCards(Snapshot?.Runtime.VideoControllers ?? []);
        SynchronizeMonitorCards(Snapshot?.Runtime.Monitors ?? []);
        SynchronizeMonitorResolutionCards(BuildMonitorResolutionCardData());
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

    private void UpdateCpuClockHistoryAxis()
    {
        var historyPoints = CpuClockHistory
            .Select(point => point.DateTime)
            .OrderBy(point => point)
            .ToArray();

        var axisEnd = historyPoints.Length == 0
            ? DateTime.Now
            : historyPoints[^1] > DateTime.Now ? historyPoints[^1] : DateTime.Now;

        var earliestPoint = historyPoints.Length == 0
            ? axisEnd - CpuClockHistoryWindow
            : historyPoints[0];

        var axisStart = earliestPoint < axisEnd - CpuClockHistoryWindow
            ? axisEnd - CpuClockHistoryWindow
            : earliestPoint;

        var separatorStep = GetCpuHistorySeparatorStep(axisEnd - axisStart);

        CpuClockHistoryMinLimit = axisStart.Ticks;
        CpuClockHistoryMaxLimit = axisEnd.Ticks;

        List<double> separators = [axisStart.Ticks];
        for (var tick = axisStart + separatorStep; tick < axisEnd; tick += separatorStep)
        {
            separators.Add(tick.Ticks);
        }

        if (separators.Count == 0 || separators[^1] != axisEnd.Ticks)
        {
            separators.Add(axisEnd.Ticks);
        }

        CpuClockHistorySeparators = [.. separators];
    }

    private TimeSpan GetCpuHistorySeparatorStep(TimeSpan visibleSpan)
    {
        if (visibleSpan <= TimeSpan.FromMinutes(1))
        {
            return TimeSpan.FromSeconds(5);
        }

        if (visibleSpan <= TimeSpan.FromMinutes(5))
        {
            return TimeSpan.FromSeconds(30);
        }

        if (visibleSpan <= TimeSpan.FromMinutes(15))
        {
            return TimeSpan.FromMinutes(1);
        }

        if (visibleSpan <= TimeSpan.FromMinutes(30))
        {
            return TimeSpan.FromMinutes(5);
        }

        return TimeSpan.FromMinutes(10);
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

    private void SynchronizeCpuPackageCards(IReadOnlyList<HardwareInfoCpu> cpus)
    {
        SynchronizeCards(
            _cpuPackageCards,
            cpus,
            static (index, cpu) => new DeviceCapabilitiesCpuPackageCardModel(index, cpu),
            static (card, index, cpu) =>
            {
                card.Index = index;
                card.Snapshot = cpu;
            });
    }

    private void SynchronizeMemoryModuleCards(IReadOnlyList<HardwareInfoMemoryModule> memoryModules)
    {
        SynchronizeCards(
            _memoryModuleCards,
            memoryModules,
            static (_, memoryModule) => new DeviceCapabilitiesMemoryModuleCardModel(memoryModule),
            static (card, _, memoryModule) => card.Snapshot = memoryModule);
    }

    private void SynchronizeStorageDriveCards(IReadOnlyList<HardwareInfoDrive> drives)
    {
        SynchronizeCards(
            _storageDriveCards,
            drives,
            static (_, drive) => new DeviceCapabilitiesStorageDriveCardModel(drive),
            static (card, _, drive) => card.Snapshot = drive);
    }

    private void SynchronizeNetworkAdapterCards(IReadOnlyList<HardwareInfoNetworkAdapter> networkAdapters)
    {
        SynchronizeCards(
            _networkAdapterCards,
            networkAdapters,
            static (_, networkAdapter) => new DeviceCapabilitiesNetworkAdapterCardModel(networkAdapter),
            static (card, _, networkAdapter) => card.Snapshot = networkAdapter);
    }

    private void SynchronizeVideoControllerCards(IReadOnlyList<HardwareInfoVideoController> videoControllers)
    {
        SynchronizeCards(
            _videoControllerCards,
            videoControllers,
            static (_, videoController) => new DeviceCapabilitiesVideoControllerCardModel(videoController),
            static (card, _, videoController) => card.Snapshot = videoController);
    }

    private void SynchronizeMonitorCards(IReadOnlyList<HardwareInfoMonitor> monitors)
    {
        SynchronizeCards(
            _monitorCards,
            monitors,
            static (_, monitor) => new DeviceCapabilitiesMonitorCardModel(monitor),
            static (card, _, monitor) => card.Snapshot = monitor);
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

    private void SynchronizeMonitorResolutionCards((string Title, string ResolutionTier)[] cards)
    {
        SynchronizeCards(
            _monitorResolutionCards,
            cards,
            static (_, card) => new DeviceCapabilitiesMonitorResolutionCard(card.Title, card.ResolutionTier),
            static (existingCard, _, card) =>
            {
                existingCard.Title = card.Title;
                existingCard.ResolutionTier = card.ResolutionTier;
            });
    }

    private void RefreshTemperatureStatusItems()
    {
        var sensors = _temperatureSnapshots.Values
            .OrderBy(snapshot => snapshot.SensorIndex)
            .ToArray();

        SynchronizeCards(
            _temperatureStatusItems,
            sensors,
            (_, snapshot) => new DeviceCapabilitiesRuntimeStatusItemModel(
                $"Sensor {snapshot.SensorIndex}",
                GetTemperatureStatus(snapshot),
                GetStatusForegroundBrush(GetTemperatureStatus(snapshot))),
            (card, _, snapshot) =>
            {
                card.Name = $"Sensor {snapshot.SensorIndex}";
                card.Status = GetTemperatureStatus(snapshot);
                card.StatusForegroundBrush = GetStatusForegroundBrush(card.Status);
            });
    }

    private void RefreshFanStatusItems()
    {
        var fanEntries = _fanSnapshots.Keys
            .Union(_fanStateSnapshots.Keys)
            .OrderBy(index => index)
            .Select(index => (
                Index: index,
                Snapshot: _fanSnapshots.GetValueOrDefault(index),
                State: _fanStateSnapshots.GetValueOrDefault(index)))
            .ToArray();

        SynchronizeCards(
            _fanStatusItems,
            fanEntries,
            (_, fan) => new DeviceCapabilitiesRuntimeStatusItemModel(
                $"Fan {fan.Index}",
                GetFanStatus(fan.Snapshot, fan.State),
                GetStatusForegroundBrush(GetFanStatus(fan.Snapshot, fan.State))),
            (card, _, fan) =>
            {
                card.Name = $"Fan {fan.Index}";
                card.Status = GetFanStatus(fan.Snapshot, fan.State);
                card.StatusForegroundBrush = GetStatusForegroundBrush(card.Status);
            });
    }

    private void RefreshBatteryStatusItems()
    {
        var batteries = _batterySnapshots.Values
            .OrderBy(snapshot => snapshot.BatteryIndex)
            .ToArray();

        SynchronizeCards(
            _batteryStatusItems,
            batteries,
            (_, snapshot) => new DeviceCapabilitiesRuntimeStatusItemModel(
                $"Battery {snapshot.BatteryIndex}",
                GetBatteryStatus(snapshot),
                GetStatusForegroundBrush(GetBatteryStatus(snapshot))),
            (card, _, snapshot) =>
            {
                card.Name = $"Battery {snapshot.BatteryIndex}";
                card.Status = GetBatteryStatus(snapshot);
                card.StatusForegroundBrush = GetStatusForegroundBrush(card.Status);
            });
    }

    private (string Title, string ResolutionTier)[] BuildMonitorResolutionCardData()
    {
        var activeControllers = Snapshot?.Runtime.VideoControllers
            .Where(HasActiveDisplay)
            .ToArray()
            ?? [];

        var monitors = Snapshot?.Runtime.Monitors ?? [];
        if (monitors.Length == 0)
        {
            return [
                .. activeControllers.Select((controller, index) => (
                    Title: $"Monitor {index}",
                    ResolutionTier: GetDisplayTierLabel(controller)))
            ];
        }

        var cards = new List<(string Title, string ResolutionTier)>(Math.Max(monitors.Length, activeControllers.Length));
        var nextActiveControllerIndex = 0;

        for (var monitorIndex = 0; monitorIndex < monitors.Length; monitorIndex++)
        {
            var monitor = monitors[monitorIndex];
            var resolutionTier = monitor.Active
                ? nextActiveControllerIndex < activeControllers.Length
                    ? GetDisplayTierLabel(activeControllers[nextActiveControllerIndex++])
                    : "Unknown"
                : "Inactive";

            cards.Add(($"Monitor {monitorIndex}", resolutionTier));
        }

        while (nextActiveControllerIndex < activeControllers.Length)
        {
            cards.Add((
                $"Monitor {cards.Count}",
                GetDisplayTierLabel(activeControllers[nextActiveControllerIndex++])));
        }

        return [.. cards];
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

    private Brush GetStatusForegroundBrush(string status)
    {
        if (status.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Stalled", StringComparison.OrdinalIgnoreCase))
        {
            return GetBrush("StatusErrorBrush", ColorHelper.FromArgb(255, 68, 39, 38));
        }

        if (status.Contains("Not Present", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Not Powered", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Not Calibrated", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Checking", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return GetBrush("StatusWarningBrush", ColorHelper.FromArgb(255, 197, 153, 78));
        }

        return GetBrush("StatusSuccessBrush", ColorHelper.FromArgb(255, 108, 203, 95));
    }

    private void SynchronizeCards<TCard, TSource>(
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
                update(target[index], index, item);
                continue;
            }

            target.Add(create(index, item));
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    private Brush GetBrush(string resourceKey, Windows.UI.Color fallbackColor)
    {
        return Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            && resource is Brush brush
                ? brush
                : new SolidColorBrush(fallbackColor);
    }
}
