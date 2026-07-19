using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services.Units;
using SubZeroFramework.Models;
using SubZeroFramework.Themes;

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
        nameof(FirmwareRevisionDisplay),
        nameof(UsagePercent),
        nameof(FreeSpaceBrush),
        nameof(UsageBarBrush))]
    public partial HardwareInfoDrive Snapshot { get; set; } = default!;

    partial void OnSnapshotChanged(HardwareInfoDrive value) => RefreshUnitFormatting();

    public string Title => FirstNonEmpty(Snapshot.Model, Snapshot.Name, Snapshot.Caption, Snapshot.Description)
        ?? $"Drive {Snapshot.Index}";

    public string DriveLabel => $"Drive {Snapshot.Index}";

    public string ManufacturerDisplay => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string MediaTypeDisplay => FirstNonEmpty(Snapshot.MediaType) ?? "Unknown";

    /// <summary>Formatted total capacity. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string CapacityDisplay { get; private set; } = string.Empty;

    public string FirmwareRevisionDisplay => FirstNonEmpty(Snapshot.FirmwareRevision) ?? "Unavailable";

    /// <summary>Formatted used space. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string UsedSpaceDisplay { get; private set; } = string.Empty;

    /// <summary>Formatted free space. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string FreeSpaceDisplay { get; private set; } = string.Empty;

    public double UsagePercent => Snapshot.UsagePercent;

    /// <summary>Mockup state colour for the Free value: red when nearly full, amber when low, default otherwise.</summary>
    public Brush FreeSpaceBrush => FreePercentBrush(Snapshot.Size == 0 ? null : 100d - Snapshot.UsagePercent);

    internal static Brush FreePercentBrush(double? freePercent) => freePercent switch
    {
        null => AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusWarningColor),
        <= 3d => AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor),
        <= 12d => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
        _ => AppThemeBrushes.Get("TextPrimaryBrush", AppThemeBrushes.StatusWarningColor),
    };

    /// <summary>Mockup state colour for the usage bar: green when healthy, amber when filling, red when nearly full.</summary>
    public Brush UsageBarBrush => Snapshot.Size == 0
        ? AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusWarningColor)
        : Snapshot.UsagePercent switch
        {
            < 75d => AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor),
            < 90d => AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor),
            _ => AppThemeBrushes.Get("StatusErrorTextBrush", AppThemeBrushes.StatusErrorColor),
        };

    /// <summary>Combined used/free line. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string UsageSummary { get; private set; } = string.Empty;

    /// <summary>
    /// Recomputes and ASSIGNS the stored unit-formatted projections so PropertyChanged is raised only for
    /// values that actually changed. Called when the snapshot updates and when the display units change.
    /// </summary>
    public void RefreshUnitFormatting()
    {
        CapacityDisplay = _unitFormattingService.FormatInformationBytes(Snapshot.Size, treatZeroAsUnknown: true);
        UsedSpaceDisplay = Snapshot.Size == 0
            ? "Unknown"
            : _unitFormattingService.FormatInformationBytes(Snapshot.UsedSpace);
        FreeSpaceDisplay = Snapshot.Size == 0
            ? "Unknown"
            : _unitFormattingService.FormatInformationBytes(Snapshot.ClampedFreeSpace);
        UsageSummary = Snapshot.Size == 0
            ? "Unknown"
            : $"{UsedSpaceDisplay} used / {FreeSpaceDisplay} free";
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
