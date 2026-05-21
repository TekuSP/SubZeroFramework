using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesMonitorCardModel : ObservableObject
{
    public DeviceCapabilitiesMonitorCardModel(HardwareInfoMonitor snapshot)
    {
        Snapshot = snapshot;
    }

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

    public string DisplayCurrentRefreshRate => Snapshot.DisplayCurrentRefreshRate;

    public string LinkedVideoControllersDisplay => Snapshot.DisplayLinkedVideoControllerSummary;

    public string Description => FirstNonEmpty(Snapshot.Description) ?? "Unknown";

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
