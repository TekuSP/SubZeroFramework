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
    [NotifyPropertyChangedFor(nameof(DisplayCapacity))]
    [NotifyPropertyChangedFor(nameof(MemoryType))]
    [NotifyPropertyChangedFor(nameof(DisplaySpeed))]
    [NotifyPropertyChangedFor(nameof(DisplayDataWidth))]
    [NotifyPropertyChangedFor(nameof(Manufacturer))]
    [NotifyPropertyChangedFor(nameof(FormFactor))]
    [NotifyPropertyChangedFor(nameof(PartNumber))]
    [NotifyPropertyChangedFor(nameof(SerialNumber))]
    public partial HardwareInfoMemoryModule Snapshot { get; set; } = default!;

    public string BankLabel => FirstNonEmpty(Snapshot.BankLabel) ?? "Unknown";

    public string DisplayCapacity => Snapshot.CapacityBytes == 0
        ? "Unknown"
        : _unitFormattingService.FormatInformationBytes(Snapshot.CapacityBytes);

    public string MemoryType => FirstNonEmpty(Snapshot.MemoryType) ?? "Unknown";

    public string DisplaySpeed => Snapshot.SpeedMHz > 0
        ? _unitFormattingService.FormatClockFrequencyMegahertz(Snapshot.SpeedMHz)
        : "Unknown";

    public string DisplayDataWidth => Snapshot.DisplayDataWidth;

    public string Manufacturer => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string FormFactor => FirstNonEmpty(Snapshot.FormFactor) ?? "Unknown";

    public string PartNumber => FirstNonEmpty(Snapshot.PartNumber) ?? "Unknown";

    public string SerialNumber => FirstNonEmpty(Snapshot.SerialNumber) ?? "Unknown";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCapacity))]
    [NotifyPropertyChangedFor(nameof(DisplaySpeed))]
    private partial int UnitFormattingRevision { get; set; }

    public void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;
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
