using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
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
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly ObservableCollection<DeviceCapabilitiesCpuPackageCardModel> _cpuPackageCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMemoryModuleCardModel> _memoryModuleCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesVideoControllerCardModel> _videoControllerCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMonitorCardModel> _monitorCards = [];
    private readonly ObservableCollection<DeviceCapabilitiesMonitorResolutionCard> _monitorResolutionCards = [];
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
        nameof(MonitorResolutionCards),
        nameof(SystemProfileOs),
        nameof(SystemProfileVendor),
        nameof(SystemProfileModel),
        nameof(SystemProfileOsVersion),
        nameof(SystemProfileProductName),
        nameof(SystemProfileSystemVersion),
        nameof(SystemProfileUuid),
        nameof(SystemProfileDescription),
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

    public ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel> VideoControllerCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel> MonitorCards { get; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorResolutionCard> MonitorResolutionCards { get; }

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

    public string SystemProfileOs => Snapshot?.Inventory.OperatingSystem?.Name ?? "Unknown";

    public string SystemProfileVendor => Snapshot?.Inventory.ComputerSystem?.Vendor ?? "Unknown";

    public string SystemProfileModel => FrameworkStatus?.DeviceModel
        ?? Snapshot?.Inventory.ComputerSystem?.Caption
        ?? "Unknown";

    public string SystemProfileOsVersion => Snapshot?.Inventory.OperatingSystem?.VersionString ?? "Unknown";

    public string SystemProfileProductName => FirstNonEmpty(
        Snapshot?.Inventory.ComputerSystem?.Skunumber,
        Snapshot?.Inventory.ComputerSystem?.Name,
        Snapshot?.Inventory.ComputerSystem?.Caption,
        FrameworkStatus?.DeviceModel)
        ?? "Unavailable";

    public string SystemProfileSystemVersion => Snapshot?.Inventory.ComputerSystem?.Version ?? "Unknown";

    public string SystemProfileUuid => FirstNonEmpty(Snapshot?.Inventory.ComputerSystem?.Uuid) ?? "Unavailable";

    public string SystemProfileDescription => FirstNonEmpty(
        Snapshot?.Inventory.ComputerSystem?.Description,
        Snapshot?.Inventory.ComputerSystem?.Caption)
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
    [NotifyPropertyChangedFor(nameof(SystemProfileModel), nameof(SystemProfileProductName))]
    public partial FrameworkSystemStatus? FrameworkStatus { get; set; }

    public DeviceCapabilitiesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IHardwareInfoClient hardwareInfoClient,
        IFrameworkStatusClient frameworkStatusClient,
        SynchronizationContext synchronizationContext)
    {
        _hardwareInfoClient = hardwareInfoClient;
        _frameworkStatusClient = frameworkStatusClient;
        _synchronizationContext = synchronizationContext;
        CpuPackageCards = new ReadOnlyObservableCollection<DeviceCapabilitiesCpuPackageCardModel>(_cpuPackageCards);
        MemoryModuleCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMemoryModuleCardModel>(_memoryModuleCards);
        VideoControllerCards = new ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel>(_videoControllerCards);
        MonitorCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel>(_monitorCards);
        MonitorResolutionCards = new ReadOnlyObservableCollection<DeviceCapabilitiesMonitorResolutionCard>(_monitorResolutionCards);

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

    private void RefreshCpuVisuals()
    {
        CpuClockHistory = BuildCpuClockHistory();
        UpdateCpuClockHistoryAxis();
    }

    private void RefreshSnapshotCards()
    {
        SynchronizeCpuPackageCards(Snapshot?.Runtime.Cpus ?? []);
        SynchronizeMemoryModuleCards(Snapshot?.Inventory.MemoryModules ?? []);
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
        const double OneGigabyte = 1024d * 1024d * 1024d;
        if (bytes == 0)
        {
            return "0 GB";
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
