using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;
using System.Collections.Generic;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesCpuPackageCardModel : ObservableObject
{
    public DeviceCapabilitiesCpuPackageCardModel(int index, HardwareInfoCpu snapshot)
    {
        Index = index;
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PackageLabel))]
    [NotifyPropertyChangedFor(nameof(ManufacturerDisplay))]
    [NotifyPropertyChangedFor(nameof(CurrentClockDisplay))]
    [NotifyPropertyChangedFor(nameof(MaxClockDisplay))]
    [NotifyPropertyChangedFor(nameof(PhysicalCoreCountDisplay))]
    [NotifyPropertyChangedFor(nameof(LogicalProcessorCountDisplay))]
    [NotifyPropertyChangedFor(nameof(L1CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(L2CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(L3CacheDisplay))]
    [NotifyPropertyChangedFor(nameof(SocketDisplay))]
    [NotifyPropertyChangedFor(nameof(VirtualizationDisplay))]
    public partial HardwareInfoCpu Snapshot { get; set; } = default!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(PackageLabel))]
    public partial int Index { get; set; }

    public string Title => FirstNonEmpty(Snapshot.Name, Snapshot.Caption) ?? $"CPU {Index}";

    public string PackageLabel => $"CPU {Index}";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string CurrentClockDisplay => Snapshot.DisplayCurrentClockSpeed;

    public string MaxClockDisplay => Snapshot.DisplayMaxClockSpeed;

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
}
