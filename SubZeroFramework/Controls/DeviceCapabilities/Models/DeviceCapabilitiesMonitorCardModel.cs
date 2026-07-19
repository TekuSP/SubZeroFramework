using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesMonitorCardModel : ObservableObject
{
    private readonly IUnitFormattingService _unitFormattingService;

    public DeviceCapabilitiesMonitorCardModel(int index, HardwareInfoMonitor snapshot, IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        Index = index;
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorLabel))]
    public partial int Index { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    [NotifyPropertyChangedFor(nameof(MonitorType))]
    [NotifyPropertyChangedFor(nameof(DisplayManufacturer))]
    [NotifyPropertyChangedFor(nameof(DisplayLogicalDensity))]
    [NotifyPropertyChangedFor(nameof(ProductCodeId))]
    [NotifyPropertyChangedFor(nameof(SerialNumberId))]
    [NotifyPropertyChangedFor(nameof(MonitorManufacturer))]
    [NotifyPropertyChangedFor(nameof(DisplayManufactured))]
    [NotifyPropertyChangedFor(nameof(DisplayCurrentResolution))]
    [NotifyPropertyChangedFor(nameof(LinkedVideoControllersDisplay))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(ResolutionBadge))]
    [NotifyPropertyChangedFor(nameof(ManufacturedDisplay))]
    public partial HardwareInfoMonitor Snapshot { get; set; } = default!;

    partial void OnSnapshotChanged(HardwareInfoMonitor value) => RefreshUnitFormatting();

    public string MonitorLabel => $"Monitor {Index}";

    public string DisplayName => Snapshot.DisplayName;

    public string DisplayStatus => Snapshot.DisplayStatus;

    public string MonitorType => FirstNonEmpty(Snapshot.MonitorType) ?? "Unknown";

    public string DisplayManufacturer => Snapshot.DisplayManufacturer;

    public string DisplayLogicalDensity => Snapshot.DisplayLogicalDensity;

    public string ProductCodeId => FirstNonEmpty(Snapshot.ProductCodeId) ?? "Unknown";

    public string SerialNumberId => FirstNonEmpty(Snapshot.SerialNumberId) ?? "Unknown";

    public string MonitorManufacturer => FirstNonEmpty(Snapshot.MonitorManufacturer) ?? "Unknown";

    public string DisplayManufactured => Snapshot.DisplayManufactured;

    public string DisplayCurrentResolution => Snapshot.DisplayCurrentResolution;

    /// <summary>Formatted current refresh rate. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string DisplayCurrentRefreshRate { get; private set; } = string.Empty;

    public string LinkedVideoControllersDisplay => Snapshot.DisplayLinkedVideoControllerSummary;

    public string Description => FirstNonEmpty(Snapshot.Description) ?? "Unknown";

    /// <summary>Mockup state colour for the Status value: green while the monitor is active.</summary>
    public Brush StatusBrush => Snapshot.Active
        ? AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("TextSecondaryBrush", AppThemeBrushes.StatusWarningColor);

    /// <summary>Resolution standard chip per the mockup (WQXGA / QHD …); empty when no mode is reported.</summary>
    public string ResolutionBadge => DeviceCapabilitiesResolutionBadge.For(Snapshot.CurrentHorizontalResolution, Snapshot.CurrentVerticalResolution);

    /// <summary>Manufacture stamp per the mockup, e.g. "2024 · week 12".</summary>
    public string ManufacturedDisplay => Snapshot.YearOfManufacture == 0
        ? "Unknown"
        : Snapshot.WeekOfManufacture > 0
            ? $"{Snapshot.YearOfManufacture} · week {Snapshot.WeekOfManufacture}"
            : Snapshot.YearOfManufacture.ToString();

    /// <summary>Second picker line: current mode ("2,560 x 1,600 · 165 Hz"), falling back to the manufacturer. Stored; assigned by <see cref="RefreshUnitFormatting"/>.</summary>
    [ObservableProperty]
    public partial string PickerSubtitle { get; private set; } = string.Empty;

    /// <summary>
    /// Recomputes and ASSIGNS the stored unit-formatted projections so PropertyChanged is raised only for
    /// values that actually changed. Called when the snapshot updates and when the display units change.
    /// </summary>
    public void RefreshUnitFormatting()
    {
        DisplayCurrentRefreshRate = Snapshot.CurrentRefreshRate > 0
            ? _unitFormattingService.FormatRefreshRateHertz(Snapshot.CurrentRefreshRate)
            : "Unknown";

        var resolution = DisplayCurrentResolution;
        if (resolution == "Unknown")
        {
            PickerSubtitle = DisplayManufacturer;
        }
        else
        {
            var refreshRate = DisplayCurrentRefreshRate;
            PickerSubtitle = refreshRate == "Unknown" ? resolution : $"{resolution} · {refreshRate}";
        }
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
