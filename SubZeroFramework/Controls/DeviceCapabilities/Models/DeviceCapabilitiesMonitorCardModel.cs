using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

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
    [NotifyPropertyChangedFor(nameof(DisplayCurrentRefreshRate))]
    [NotifyPropertyChangedFor(nameof(LinkedVideoControllersDisplay))]
    [NotifyPropertyChangedFor(nameof(Description))]
    public partial HardwareInfoMonitor Snapshot { get; set; } = default!;

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

    public string DisplayCurrentRefreshRate => Snapshot.CurrentRefreshRate > 0
        ? _unitFormattingService.FormatRefreshRateHertz(Snapshot.CurrentRefreshRate)
        : "Unknown";

    public string LinkedVideoControllersDisplay => Snapshot.DisplayLinkedVideoControllerSummary;

    public string Description => FirstNonEmpty(Snapshot.Description) ?? "Unknown";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCurrentRefreshRate))]
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
