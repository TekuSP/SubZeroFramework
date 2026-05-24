using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;
using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesStorageDriveCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public DeviceCapabilitiesStorageDriveCardModel(HardwareInfoDrive snapshot, IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(Title),
        nameof(DriveLabel),
        nameof(ManufacturerDisplay),
        nameof(MediaTypeDisplay),
        nameof(CapacityDisplay),
        nameof(FirmwareRevisionDisplay),
        nameof(UsedSpaceDisplay),
        nameof(FreeSpaceDisplay),
        nameof(UsagePercent),
        nameof(UsageSummary))]
    public partial HardwareInfoDrive Snapshot { get; set; } = default!;

    public string Title => FirstNonEmpty(Snapshot.Model, Snapshot.Name, Snapshot.Caption, Snapshot.Description)
        ?? $"Drive {Snapshot.Index}";

    public string DriveLabel => $"Drive {Snapshot.Index}";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string MediaTypeDisplay => FirstNonEmpty(Snapshot.MediaType) ?? "Unknown";

    public string CapacityDisplay => _unitFormattingService.FormatInformationBytes(Snapshot.Size, treatZeroAsUnknown: true);

    public string FirmwareRevisionDisplay => FirstNonEmpty(Snapshot.FirmwareRevision) ?? "Unavailable";

    public string UsedSpaceDisplay => Snapshot.Size == 0
        ? "Unknown"
        : _unitFormattingService.FormatInformationBytes(Snapshot.UsedSpace);

    public string FreeSpaceDisplay => Snapshot.Size == 0
        ? "Unknown"
        : _unitFormattingService.FormatInformationBytes(Snapshot.ClampedFreeSpace);

    public double UsagePercent => Snapshot.UsagePercent;

    public string UsageSummary => Snapshot.Size == 0
        ? "Unknown"
        : $"{UsedSpaceDisplay} used / {FreeSpaceDisplay} free";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapacityDisplay))]
    [NotifyPropertyChangedFor(nameof(UsedSpaceDisplay))]
    [NotifyPropertyChangedFor(nameof(FreeSpaceDisplay))]
    [NotifyPropertyChangedFor(nameof(UsageSummary))]
    private partial int UnitFormattingRevision { get; set; }

    public void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
