using CommunityToolkit.Mvvm.ComponentModel;
using SubZeroFramework.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesVideoControllerCardModel : ObservableObject
{
    public DeviceCapabilitiesVideoControllerCardModel(HardwareInfoVideoController snapshot)
    {
        Snapshot = snapshot;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(Manufacturer))]
    [NotifyPropertyChangedFor(nameof(VideoProcessor))]
    [NotifyPropertyChangedFor(nameof(DriverVersion))]
    [NotifyPropertyChangedFor(nameof(DisplayDriverDate))]
    [NotifyPropertyChangedFor(nameof(VideoModeDescription))]
    [NotifyPropertyChangedFor(nameof(DisplayResolution))]
    [NotifyPropertyChangedFor(nameof(ResolutionBadge))]
    [NotifyPropertyChangedFor(nameof(CurrentRefreshRateHertz))]
    [NotifyPropertyChangedFor(nameof(AdapterRamBytes))]
    [NotifyPropertyChangedFor(nameof(ConnectedMonitorCountDisplay))]
    [NotifyPropertyChangedFor(nameof(ConnectedMonitorsDisplay))]
    public partial HardwareInfoVideoController Snapshot { get; set; } = default!;

    public string Name => FirstNonEmpty(Snapshot.Name, Snapshot.Caption, Snapshot.Description) ?? "Unknown";

    public string Manufacturer => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string VideoProcessor => FirstNonEmpty(Snapshot.VideoProcessor) ?? "Unknown";

    public string DriverVersion => FirstNonEmpty(Snapshot.DriverVersion) ?? "Unknown";

    public string DisplayDriverDate => Snapshot.DisplayDriverDate;

    public string VideoModeDescription => FirstNonEmpty(Snapshot.VideoModeDescription) ?? "Unknown";

    public string DisplayResolution => Snapshot.DisplayResolution;

    /// <summary>Resolution standard chip per the mockup (WQXGA / QHD / Full HD…); empty when inactive.</summary>
    public string ResolutionBadge => DeviceCapabilitiesResolutionBadge.For(Snapshot.CurrentHorizontalResolution, Snapshot.CurrentVerticalResolution);

    /// <summary>Refresh rate in canonical hertz; formatted by UnitFormatConverter. Null when none was reported.</summary>
    public double? CurrentRefreshRateHertz => Snapshot.CurrentRefreshRateHertz;

    /// <summary>Adapter memory in canonical bytes; formatted by UnitFormatConverter. Null when the adapter reports none.</summary>
    public ulong? AdapterRamBytes => Snapshot.AdapterRAM == 0 ? null : Snapshot.AdapterRAM;

    public string ConnectedMonitorCountDisplay => Snapshot.LinkedMonitorDisplayNames.Length.ToString("N0");

    public string ConnectedMonitorsDisplay => Snapshot.DisplayLinkedMonitorSummary;

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
