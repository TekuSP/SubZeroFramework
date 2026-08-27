using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;
using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesMemoryModuleCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public DeviceCapabilitiesMemoryModuleCardModel(HardwareInfoMemoryModule snapshot, IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BankLabel))]
    [NotifyPropertyChangedFor(nameof(MemoryType))]
    [NotifyPropertyChangedFor(nameof(DisplayDataWidth))]
    [NotifyPropertyChangedFor(nameof(Manufacturer))]
    [NotifyPropertyChangedFor(nameof(FormFactor))]
    [NotifyPropertyChangedFor(nameof(PartNumber))]
    [NotifyPropertyChangedFor(nameof(SerialNumber))]
    public partial HardwareInfoMemoryModule Snapshot { get; set; } = default!;

    public string BankLabel => FirstNonEmpty(Snapshot.BankLabel) ?? "Unknown";

    /// <summary>Module capacity in canonical bytes; null when the platform does not report it.</summary>
    [ObservableProperty]
    public partial double? CapacityBytes { get; private set; }

    public string MemoryType => FirstNonEmpty(Snapshot.MemoryType) ?? "Unknown";

    /// <summary>Module speed in canonical megahertz; null when the platform does not report it.</summary>
    [ObservableProperty]
    public partial double? SpeedMegahertz { get; private set; }

    public string DisplayDataWidth => Snapshot.DisplayDataWidth;

    public string Manufacturer => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string FormFactor => FirstNonEmpty(Snapshot.FormFactor) ?? "Unknown";

    public string PartNumber => FirstNonEmpty(Snapshot.PartNumber) ?? "Unknown";

    public string SerialNumber => FirstNonEmpty(Snapshot.SerialNumber) ?? "Unknown";

    partial void OnSnapshotChanged(HardwareInfoMemoryModule value)
    {
        RefreshUnitFormatting();
    }

    /// <summary>
    /// Projects the snapshot into CANONICAL values. Formatting happens in UnitFormatConverter at render time,
    /// so a unit change needs only the notification below, not a recomputation.
    /// </summary>
    public void RefreshUnitFormatting()
    {
        CapacityBytes = Snapshot.CapacityBytes == 0 ? null : Snapshot.CapacityBytes;
        SpeedMegahertz = Snapshot.SpeedMHz > 0 ? Snapshot.SpeedMHz : null;

        OnPropertyChanged(propertyName: null);
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
