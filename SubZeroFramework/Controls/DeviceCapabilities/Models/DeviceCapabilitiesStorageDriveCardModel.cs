using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesStorageDriveCardModel : ObservableObject
{
    public DeviceCapabilitiesStorageDriveCardModel(HardwareInfoDrive snapshot)
    {
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

    public string CapacityDisplay => Snapshot.DisplaySize;

    public string FirmwareRevisionDisplay => FirstNonEmpty(Snapshot.FirmwareRevision) ?? "Unavailable";

    public string UsedSpaceDisplay => Snapshot.DisplayUsedSpace;

    public string FreeSpaceDisplay => Snapshot.DisplayFreeSpace;

    public double UsagePercent => Snapshot.UsagePercent;

    public string UsageSummary => Snapshot.Size == 0
        ? "Unknown"
        : $"{UsedSpaceDisplay} used / {FreeSpaceDisplay} free";

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
