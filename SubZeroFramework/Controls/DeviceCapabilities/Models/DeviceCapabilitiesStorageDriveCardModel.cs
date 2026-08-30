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

    /// <summary>Total capacity in canonical bytes; null when the drive reports no size.</summary>
    [ObservableProperty]
    public partial double? CapacityBytes { get; private set; }

    /// <summary>
    /// The firmware version the drive itself reports, when the operating system did not supply one.
    /// </summary>
    /// <remarks>
    /// Read from the drive over NVMe rather than from WMI, and matched to this drive by device path. Only a
    /// FALLBACK: where WMI answers, that value is kept, because it is the one every other tool on the machine
    /// shows and disagreeing with them would raise a question this card cannot settle.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareRevisionDisplay))]
    public partial string? NvmeFirmwareRevision { get; set; }

    public string FirmwareRevisionDisplay =>
        FirstNonEmpty(Snapshot.FirmwareRevision) ?? FirstNonEmpty(NvmeFirmwareRevision) ?? "Unavailable";

    /// <summary>Used space in canonical bytes.</summary>
    [ObservableProperty]
    public partial double? UsedSpaceBytes { get; private set; }

    /// <summary>Free space in canonical bytes.</summary>
    [ObservableProperty]
    public partial double? FreeSpaceBytes { get; private set; }

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
        // Canonical bytes; a drive reporting size 0 reported nothing, so all three become null and the
        // converter renders the empty state.
        var known = Snapshot.Size != 0;
        CapacityBytes = known ? Snapshot.Size : null;
        UsedSpaceBytes = known ? Snapshot.UsedSpace : null;
        FreeSpaceBytes = known ? Snapshot.ClampedFreeSpace : null;

        // A composite of two quantities, so it stays formatted here — a converter formats one value and
        // cannot join two. Same split as the monitor card's picker subtitle.
        UsageSummary = known
            ? $"{_unitFormattingService.FormatInformationBytes(Snapshot.UsedSpace)} used / "
                + $"{_unitFormattingService.FormatInformationBytes(Snapshot.ClampedFreeSpace)} free"
            : "Unknown";

        // The three canonical byte counts above do not move when the unit preference does, so the tiles bound
        // to them through the converter need the "everything changed" broadcast or they keep the old unit.
        OnPropertyChanged(propertyName: null);
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
