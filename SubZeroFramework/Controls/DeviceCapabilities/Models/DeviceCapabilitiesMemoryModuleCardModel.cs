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

    /// <summary>Formatted module capacity. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string DisplayCapacity { get; private set; } = "Unknown";

    public string MemoryType => FirstNonEmpty(Snapshot.MemoryType) ?? "Unknown";

    /// <summary>Formatted module speed. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string DisplaySpeed { get; private set; } = "Unknown";

    public string DisplayDataWidth => Snapshot.DisplayDataWidth;

    public string Manufacturer => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string FormFactor => FirstNonEmpty(Snapshot.FormFactor) ?? "Unknown";

    public string PartNumber => FirstNonEmpty(Snapshot.PartNumber) ?? "Unknown";

    public string SerialNumber => FirstNonEmpty(Snapshot.SerialNumber) ?? "Unknown";

    partial void OnSnapshotChanged(HardwareInfoMemoryModule value)
    {
        RefreshUnitFormatting();
    }

    /// <summary>Recomputes and assigns the unit-formatted projections; assignment raises PropertyChanged only for values that actually changed.</summary>
    public void RefreshUnitFormatting()
    {
        DisplayCapacity = Snapshot.CapacityBytes == 0
            ? "Unknown"
            : _unitFormattingService.FormatInformationBytes(Snapshot.CapacityBytes);

        DisplaySpeed = Snapshot.SpeedMHz > 0
            ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.SpeedMHz)
            : "Unknown";
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
