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

    public string Title => FirstNonEmpty(Snapshot.Name, Snapshot.Caption) ?? $"CPU {Index}";

    public string PackageLabel => $"CPU {Index}";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    // Canonical megahertz, formatted by UnitFormatConverter. Null rather than zero for "the platform did not
    // report it", so the converter renders the empty state instead of a plausible-looking 0 MHz.
    [ObservableProperty]
    public partial double? CurrentClockMegahertz { get; private set; }

    [ObservableProperty]
    public partial double? MaxClockMegahertz { get; private set; }

    [ObservableProperty]
    public partial double CpuUsageAxisMaxLimit { get; private set; }

    public string PhysicalCoreCountDisplay => Snapshot.Cores > 0
        ? Snapshot.Cores.ToString("N0")
        : "Unknown";

    public string LogicalProcessorCountDisplay => Snapshot.LogicalProcessors > 0
        ? Snapshot.LogicalProcessors.ToString("N0")
        : "Unknown";

    // Canonical BYTES, not the kilobytes the snapshot carries, so cache sizes go through the same
    // InformationSize converter as every other size in the app and follow the binary/decimal preference.
    [ObservableProperty]
    public partial double? L1CacheBytes { get; private set; }

    [ObservableProperty]
    public partial double? L2CacheBytes { get; private set; }

    [ObservableProperty]
    public partial double? L3CacheBytes { get; private set; }

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
        // The usage axis max follows the unit preference; the snapshot displays reformat under it too.
        CpuUsageAxisMaxLimit = _unitFormattingService.RatioAxisMaximum;
        RefreshSnapshotDisplays();

        foreach (var cpuCoreItem in _cpuCoreItems)
        {
            cpuCoreItem.RefreshUnitFormatting();
        }
    }

    // Projects the snapshot into CANONICAL values; formatting happens in the converter at render time. A
    // zero from the platform means "not reported" for all of these, so it becomes null rather than a figure.
    private void RefreshSnapshotDisplays()
    {
        CurrentClockMegahertz = Snapshot.CurrentClockSpeedMHz > 0 ? Snapshot.CurrentClockSpeedMHz : null;
        MaxClockMegahertz = Snapshot.MaxClockSpeedMHz > 0 ? Snapshot.MaxClockSpeedMHz : null;
        L1CacheBytes = ToCacheBytes(Snapshot.L1CacheSizeKb);
        L2CacheBytes = ToCacheBytes(Snapshot.L2CacheSizeKb);
        L3CacheBytes = ToCacheBytes(Snapshot.L3CacheSizeKb);

        // Live figures rather than inventory, so unlike the rest of this card they can legitimately be absent
        // — a machine whose firmware exposes no energy meter reports no package power, and the tile shows the
        // unknown dash rather than a zero that would read as an idle processor.
        PackagePowerWatts = Snapshot.PackagePowerWatts;
        PackageUsagePercent = Snapshot.EffectivePercentProcessorTime;
    }

    /// <summary>Processor package power, canonical watts; null where the platform reports no energy meter.</summary>
    [ObservableProperty]
    public partial double? PackagePowerWatts { get; private set; }

    /// <summary>Package-wide processor usage, 0–100; the same figure the per-core row averages to.</summary>
    [ObservableProperty]
    public partial double? PackageUsagePercent { get; private set; }

    private static double? ToCacheBytes(int kilobytes) => kilobytes > 0 ? kilobytes * 1024d : null;

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

    // Cache sizes are projected to canonical bytes by ToCacheBytes and formatted by the InformationSize
    // converter, so there is no kilobyte-specific formatting left here.

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
