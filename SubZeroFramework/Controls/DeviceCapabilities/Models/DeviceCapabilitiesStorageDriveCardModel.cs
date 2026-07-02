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
        nameof(CapacityDisplay),
        nameof(FirmwareRevisionDisplay),
        nameof(UsedSpaceDisplay),
        nameof(FreeSpaceDisplay),
        nameof(UsagePercent),
        nameof(UsageSummary),
        nameof(FreeSpaceBrush),
        nameof(UsageBarBrush))]
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
