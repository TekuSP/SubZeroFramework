using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore.Defaults;
using Microsoft.UI.Xaml;
using SubZeroFramework.Models;
using SubZeroFramework.Presentation;
using SubZeroFramework.Services.Units;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesCpuPackageCardModel : ObservableObject
{
    private readonly ObservableCollection<DeviceCapabilitiesCpuCoreItemModel> _cpuCoreItems = [];
    private readonly IUnitFormattingService _unitFormattingService;

    public DeviceCapabilitiesCpuPackageCardModel(int index, HardwareInfoCpu snapshot, IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
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

    public Func<double, string> CpuUsageLabelFormatter => _unitFormattingService.FormatRatioAxisLabel;

    public Func<double, string> CpuClockLabelFormatter => _unitFormattingService.FormatClockFrequencyAxisLabel;

    public string Title => FirstNonEmpty(Snapshot.Name, Snapshot.Caption) ?? $"CPU {Index}";

    public string PackageLabel => $"CPU {Index}";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string CurrentClockDisplay => Snapshot.CurrentClockSpeedMHz > 0
        ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.CurrentClockSpeedMHz)
        : "Unknown";

    public string MaxClockDisplay => Snapshot.MaxClockSpeedMHz > 0
        ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.MaxClockSpeedMHz)
        : "Unknown";

    public string AverageCpuUsageDisplay => Snapshot.EffectivePercentProcessorTime is double value
        ? _unitFormattingService.FormatRatio(Math.Clamp(value, 0d, 100d), decimals: 1)
        : "Unknown";

    public double CpuUsageAxisMaxLimit => _unitFormattingService.RatioAxisMaximum;

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

    [ObservableProperty]
    public partial DateTimePoint[] CpuUsageHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] CpuUsageHistorySeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? CpuUsageHistoryMinLimit { get; set; }

    [ObservableProperty]
    public partial double? CpuUsageHistoryMaxLimit { get; set; }

    [ObservableProperty]
    public partial DateTimePoint[] CpuClockHistory { get; set; } = [];

    [ObservableProperty]
    public partial double[] CpuClockHistorySeparators { get; set; } = [];

    [ObservableProperty]
    public partial double? CpuClockHistoryMinLimit { get; set; }

    [ObservableProperty]
    public partial double? CpuClockHistoryMaxLimit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentClockDisplay))]
    [NotifyPropertyChangedFor(nameof(MaxClockDisplay))]
    [NotifyPropertyChangedFor(nameof(AverageCpuUsageDisplay))]
    [NotifyPropertyChangedFor(nameof(L1CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(L2CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(L3CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(CpuUsageLabelFormatter))]
    [NotifyPropertyChangedFor(nameof(CpuClockLabelFormatter))]
    [NotifyPropertyChangedFor(nameof(CpuUsageAxisMaxLimit))]
    private partial int UnitFormattingRevision { get; set; }

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public string RecentTelemetryHistoryWindowDisplay => PresentationDefaults.RecentTelemetryHistoryWindowLabel;

    public string CpuClockHistoryWindowDisplay => PresentationDefaults.RecentTelemetryHistoryWindowLabel;

    partial void OnSnapshotChanged(HardwareInfoCpu value)
    {
        SynchronizeCpuCoreItems(value.CpuCores);
    }

    public void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;

        foreach (var cpuCoreItem in _cpuCoreItems)
        {
            cpuCoreItem.RefreshUnitFormatting();
        }
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
        return _unitFormattingService.FormatInformationKilobytes(kilobytes);
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

            _cpuCoreItems.Add(new DeviceCapabilitiesCpuCoreItemModel(cpuCore, _unitFormattingService));
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
