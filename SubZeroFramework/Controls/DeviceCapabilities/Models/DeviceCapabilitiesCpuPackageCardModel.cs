using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore.Defaults;
using Microsoft.UI.Xaml;
using SubZeroFramework.Models;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesCpuPackageCardModel : ObservableObject
{
    private readonly ObservableCollection<DeviceCapabilitiesCpuCoreItemModel> _cpuCoreItems = [];

    public DeviceCapabilitiesCpuPackageCardModel(int index, HardwareInfoCpu snapshot)
    {
        CpuCoreItems = new ReadOnlyObservableCollection<DeviceCapabilitiesCpuCoreItemModel>(_cpuCoreItems);
        Index = index;
        Snapshot = snapshot;
        SynchronizeCpuCoreItems(snapshot.CpuCores);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PackageLabel))]
    [NotifyPropertyChangedFor(nameof(ManufacturerDisplay))]
    [NotifyPropertyChangedFor(nameof(CurrentClockDisplay))]
    [NotifyPropertyChangedFor(nameof(MaxClockDisplay))]
    [NotifyPropertyChangedFor(nameof(AverageCpuUsageDisplay))]
    [NotifyPropertyChangedFor(nameof(PhysicalCoreCountDisplay))]
    [NotifyPropertyChangedFor(nameof(LogicalProcessorCountDisplay))]
    [NotifyPropertyChangedFor(nameof(L1CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(L2CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(L3CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(SocketDisplay))]
    [NotifyPropertyChangedFor(nameof(VirtualizationDisplay))]
    [NotifyPropertyChangedFor(nameof(HasCpuCoreDetails))]
    [NotifyPropertyChangedFor(nameof(CpuCoreCountDisplay))]
    [NotifyPropertyChangedFor(nameof(CpuCoreDetailsVisibility))]
    public partial HardwareInfoCpu Snapshot { get; set; } = default!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PackageLabel))]
    public partial int Index { get; set; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesCpuCoreItemModel> CpuCoreItems { get; }

    public string Title => FirstNonEmpty(Snapshot.Name, Snapshot.Caption) ?? $"CPU {Index}";

    public string PackageLabel => $"CPU {Index}";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string CurrentClockDisplay => Snapshot.DisplayCurrentClockSpeed;

    public string MaxClockDisplay => Snapshot.DisplayMaxClockSpeed;

    public string AverageCpuUsageDisplay => Snapshot.EffectivePercentProcessorTime is double value
        ? $"{Math.Clamp(value, 0d, 100d):0.#} %"
        : "Unknown";

    public string PhysicalCoreCountDisplay => Snapshot.Cores > 0
        ? Snapshot.Cores.ToString("N0")
        : "Unknown";

    public string LogicalProcessorCountDisplay => Snapshot.LogicalProcessors > 0
        ? Snapshot.LogicalProcessors.ToString("N0")
        : "Unknown";

    public string L1CacheDisplay => FormatCpuCacheSize(Snapshot.L1CacheSizeKb);

    public string L2CacheDisplay => FormatCpuCacheSize(Snapshot.L2CacheSizeKb);

    public string L3CacheDisplay => FormatCpuCacheSize(Snapshot.L3CacheSizeKb);

    public string SocketDisplay => FirstNonEmpty(Snapshot.SocketDesignation) ?? "Unavailable";

    public string VirtualizationDisplay => BuildVirtualizationDisplay();

    public bool HasCpuCoreDetails => Snapshot.HasCpuCoreDetails;

    public string CpuCoreCountDisplay => Snapshot.CpuCores.Length.ToString("N0");

    public Visibility CpuCoreDetailsVisibility => HasCpuCoreDetails
        ? Visibility.Visible
        : Visibility.Collapsed;

    private DateTimePoint[] _cpuUsageHistory = [];

    public DateTimePoint[] CpuUsageHistory
    {
        get => _cpuUsageHistory;
        set => SetProperty(ref _cpuUsageHistory, value);
    }

    private double[] _cpuUsageHistorySeparators = [];

    public double[] CpuUsageHistorySeparators
    {
        get => _cpuUsageHistorySeparators;
        set => SetProperty(ref _cpuUsageHistorySeparators, value);
    }

    private double? _cpuUsageHistoryMinLimit;

    public double? CpuUsageHistoryMinLimit
    {
        get => _cpuUsageHistoryMinLimit;
        set => SetProperty(ref _cpuUsageHistoryMinLimit, value);
    }

    private double? _cpuUsageHistoryMaxLimit;

    public double? CpuUsageHistoryMaxLimit
    {
        get => _cpuUsageHistoryMaxLimit;
        set => SetProperty(ref _cpuUsageHistoryMaxLimit, value);
    }

    private DateTimePoint[] _cpuClockHistory = [];

    public DateTimePoint[] CpuClockHistory
    {
        get => _cpuClockHistory;
        set => SetProperty(ref _cpuClockHistory, value);
    }

    private double[] _cpuClockHistorySeparators = [];

    public double[] CpuClockHistorySeparators
    {
        get => _cpuClockHistorySeparators;
        set => SetProperty(ref _cpuClockHistorySeparators, value);
    }

    private double? _cpuClockHistoryMinLimit;

    public double? CpuClockHistoryMinLimit
    {
        get => _cpuClockHistoryMinLimit;
        set => SetProperty(ref _cpuClockHistoryMinLimit, value);
    }

    private double? _cpuClockHistoryMaxLimit;

    public double? CpuClockHistoryMaxLimit
    {
        get => _cpuClockHistoryMaxLimit;
        set => SetProperty(ref _cpuClockHistoryMaxLimit, value);
    }

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public string RecentTelemetryHistoryWindowDisplay => Presentation.PresentationDefaults.RecentTelemetryHistoryWindow.TotalMinutes >= 1d
        ? $"Last {Presentation.PresentationDefaults.RecentTelemetryHistoryWindow.TotalMinutes:0} minutes"
        : $"Last {Presentation.PresentationDefaults.RecentTelemetryHistoryWindow.TotalSeconds:0} seconds";



    public string CpuClockHistoryWindowDisplay => Presentation.PresentationDefaults.RecentTelemetryHistoryWindow.TotalMinutes >= 1d
        ? $"Last {Presentation.PresentationDefaults.RecentTelemetryHistoryWindow.TotalMinutes:0} minutes"
        : $"Last {Presentation.PresentationDefaults.RecentTelemetryHistoryWindow.TotalSeconds:0} seconds";


    partial void OnSnapshotChanged(HardwareInfoCpu value)
    {
        SynchronizeCpuCoreItems(value.CpuCores);
    }

    private string BuildVirtualizationDisplay()
    {
        List<string> capabilities = [];

        if (Snapshot.VirtualizationFirmwareEnabled)
        {
            capabilities.Add("Firmware enabled");
        }

        if (Snapshot.SecondLevelAddressTranslationExtensions)
        {
            capabilities.Add("SLAT");
        }

        if (Snapshot.VMMonitorModeExtensions)
        {
            capabilities.Add("VM monitor");
        }

        return capabilities.Count > 0
            ? string.Join(" / ", capabilities)
            : "Not reported";
    }

    private string FormatCpuCacheSize(int kilobytes)
    {
        if (kilobytes <= 0)
        {
            return "Unavailable";
        }

        if (kilobytes >= 1024)
        {
            return $"{kilobytes / 1024d:0.##} MB";
        }

        return $"{kilobytes:N0} KB";
    }

    private string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private void SynchronizeCpuCoreItems(IReadOnlyList<HardwareInfoCpuCore> cpuCores)
    {
        for (var coreIndex = 0; coreIndex < cpuCores.Count; coreIndex++)
        {
            var cpuCore = cpuCores[coreIndex];
            if (coreIndex < _cpuCoreItems.Count)
            {
                _cpuCoreItems[coreIndex].Snapshot = cpuCore;
                continue;
            }

            _cpuCoreItems.Add(new DeviceCapabilitiesCpuCoreItemModel(cpuCore));
        }

        while (_cpuCoreItems.Count > cpuCores.Count)
        {
            _cpuCoreItems.RemoveAt(_cpuCoreItems.Count - 1);
        }
    }

    public void UpdateCpuCoreUsageHistory(
        int coreIndex,
        IReadOnlyList<DateTimePoint> usageHistory,
        double? usageMinLimit,
        double? usageMaxLimit,
        IReadOnlyList<double> usageSeparators)
    {
        if (coreIndex < 0 || coreIndex >= _cpuCoreItems.Count)
        {
            return;
        }

        _cpuCoreItems[coreIndex].UpdateHistory(usageHistory, usageMinLimit, usageMaxLimit, usageSeparators);
    }

    public void UpdateCpuUsageHistory(
        IReadOnlyList<DateTimePoint> usageHistory,
        double? usageMinLimit,
        double? usageMaxLimit,
        IReadOnlyList<double> usageSeparators)
    {
        CpuUsageHistory = [.. usageHistory];
        CpuUsageHistoryMinLimit = usageMinLimit;
        CpuUsageHistoryMaxLimit = usageMaxLimit;
        CpuUsageHistorySeparators = [.. usageSeparators];
    }

    public void UpdateCpuClockHistory(
        IReadOnlyList<DateTimePoint> clockHistory,
        double? clockMinLimit,
        double? clockMaxLimit,
        IReadOnlyList<double> clockSeparators)
    {
        CpuClockHistory = [.. clockHistory];
        CpuClockHistoryMinLimit = clockMinLimit;
        CpuClockHistoryMaxLimit = clockMaxLimit;
        CpuClockHistorySeparators = [.. clockSeparators];
    }

    private static string FormatHistoryWindowDisplay(IReadOnlyList<DateTimePoint> history)
    {
        if (history.Count < 2)
        {
            return "Recent retained history";
        }

        var span = history[^1].DateTime - history[0].DateTime;
        if (span.TotalSeconds < 1d)
        {
            return "Current sample";
        }

        if (span.TotalMinutes < 1d)
        {
            var seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds, MidpointRounding.AwayFromZero));
            return $"Last {seconds} second{(seconds == 1 ? string.Empty : "s")}";
        }

        if (span.TotalHours < 1d)
        {
            var minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));
            return $"Last {minutes} minute{(minutes == 1 ? string.Empty : "s")}";
        }

        var hours = Math.Max(1, (int)Math.Round(span.TotalHours, MidpointRounding.AwayFromZero));
        return $"Last {hours} hour{(hours == 1 ? string.Empty : "s")}";
    }

    private static string Formatter(DateTime date)
    {
        var elapsed = DateTime.Now - date;

        if (elapsed.TotalSeconds < 1d)
        {
            return "now";
        }

        if (elapsed.TotalMinutes < 1d)
        {
            return $"{elapsed.TotalSeconds:N0}s";
        }

        if (elapsed.TotalHours < 1d)
        {
            return $"{elapsed.TotalMinutes:N0}m";
        }

        var hours = (int)Math.Floor(elapsed.TotalHours);
        return $"{hours}h";
    }
}
