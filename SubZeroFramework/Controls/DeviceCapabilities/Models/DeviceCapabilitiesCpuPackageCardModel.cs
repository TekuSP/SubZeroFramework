using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore.Defaults;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SubZeroFramework.Models;
using SubZeroFramework.Presentation;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;
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
        CpuUsageLabelFormatter = CreateCpuUsageLabelFormatter();
        CpuClockLabelFormatter = CreateCpuClockLabelFormatter();
        CpuUsageAxisMaxLimit = unitFormattingService.RatioAxisMaximum;
        Index = index;
        Snapshot = snapshot;
        SynchronizeCpuCoreItems(snapshot.CpuCores);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PackageLabel))]
    [NotifyPropertyChangedFor(nameof(ManufacturerDisplay))]
    [NotifyPropertyChangedFor(nameof(PhysicalCoreCountDisplay))]
    [NotifyPropertyChangedFor(nameof(LogicalProcessorCountDisplay))]
    [NotifyPropertyChangedFor(nameof(SocketDisplay))]
    [NotifyPropertyChangedFor(nameof(VirtualizationDisplay))]
    [NotifyPropertyChangedFor(nameof(VirtualizationBrush))]
    [NotifyPropertyChangedFor(nameof(HasCpuCoreDetails))]
    [NotifyPropertyChangedFor(nameof(CpuCoreCountDisplay))]
    [NotifyPropertyChangedFor(nameof(CpuCoreDetailsVisibility))]
    public partial HardwareInfoCpu Snapshot { get; set; } = default!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PackageLabel))]
    public partial int Index { get; set; }

    public ReadOnlyObservableCollection<DeviceCapabilitiesCpuCoreItemModel> CpuCoreItems { get; }

    [ObservableProperty]
    public partial Func<double, string> CpuUsageLabelFormatter { get; private set; }

    [ObservableProperty]
    public partial Func<double, string> CpuClockLabelFormatter { get; private set; }

    public string Title => FirstNonEmpty(Snapshot.Name, Snapshot.Caption) ?? $"CPU {Index}";

    public string PackageLabel => $"CPU {Index}";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    [ObservableProperty]
    public partial string CurrentClockDisplay { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string MaxClockDisplay { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string AverageCpuUsageDisplay { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial double CpuUsageAxisMaxLimit { get; private set; }

    public string PhysicalCoreCountDisplay => Snapshot.Cores > 0
        ? Snapshot.Cores.ToString("N0")
        : "Unknown";

    public string LogicalProcessorCountDisplay => Snapshot.LogicalProcessors > 0
        ? Snapshot.LogicalProcessors.ToString("N0")
        : "Unknown";

    [ObservableProperty]
    public partial string L1CacheDisplay { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string L2CacheDisplay { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial string L3CacheDisplay { get; private set; } = "Unknown";

    public string SocketDisplay => FirstNonEmpty(Snapshot.SocketDesignation) ?? "Unavailable";

    public string VirtualizationDisplay => BuildVirtualizationDisplay();

    /// <summary>Mockup state colour: green when virtualization is firmware-enabled.</summary>
    public Brush VirtualizationBrush => VirtualizationDisplay.Contains("enabled", StringComparison.OrdinalIgnoreCase)
        ? AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusWarningColor);

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

    public Func<DateTime, string> LabelsFormatter { get; } = Formatter;

    public string RecentTelemetryHistoryWindowDisplay => PresentationDefaults.RecentTelemetryHistoryWindowLabel;

    public string CpuClockHistoryWindowDisplay => PresentationDefaults.RecentTelemetryHistoryWindowLabel;

    partial void OnSnapshotChanged(HardwareInfoCpu value)
    {
        SynchronizeCpuCoreItems(value.CpuCores);
        RefreshSnapshotDisplays();
    }

    public void RefreshUnitFormatting()
    {
        // The axis formatters + max follow the unit preference; the snapshot displays reformat under it too.
        CpuUsageLabelFormatter = CreateCpuUsageLabelFormatter();
        CpuClockLabelFormatter = CreateCpuClockLabelFormatter();
        CpuUsageAxisMaxLimit = _unitFormattingService.RatioAxisMaximum;
        RefreshSnapshotDisplays();

        foreach (var cpuCoreItem in _cpuCoreItems)
        {
            cpuCoreItem.RefreshUnitFormatting();
        }
    }

    // Reassigns the unit-formatted snapshot displays (changes when the snapshot or the unit preference does);
    // stored-property setters raise PropertyChanged only for values that actually changed.
    private void RefreshSnapshotDisplays()
    {
        CurrentClockDisplay = Snapshot.CurrentClockSpeedMHz > 0
            ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.CurrentClockSpeedMHz)
            : "Unknown";
        MaxClockDisplay = Snapshot.MaxClockSpeedMHz > 0
            ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.MaxClockSpeedMHz)
            : "Unknown";
        AverageCpuUsageDisplay = Snapshot.EffectivePercentProcessorTime is double value
            ? _unitFormattingService.FormatRatio(Math.Clamp(value, 0d, 100d), decimals: 1)
            : "Unknown";
        L1CacheDisplay = FormatCpuCacheSize(Snapshot.L1CacheSizeKb);
        L2CacheDisplay = FormatCpuCacheSize(Snapshot.L2CacheSizeKb);
        L3CacheDisplay = FormatCpuCacheSize(Snapshot.L3CacheSizeKb);
    }

    // Fresh closures per call so the assignments never no-op (delegates over the same method/target compare
    // equal); capturing a local gives each a new target, so PropertyChanged fires and the axes rebind.
    private Func<double, string> CreateCpuUsageLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatRatioAxisLabel(value);
    }

    private Func<double, string> CreateCpuClockLabelFormatter()
    {
        var unitFormattingService = _unitFormattingService;
        return value => unitFormattingService.FormatClockFrequencyAxisLabel(value);
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
