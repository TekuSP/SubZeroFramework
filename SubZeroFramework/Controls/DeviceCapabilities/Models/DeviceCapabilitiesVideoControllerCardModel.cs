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
    [NotifyPropertyChangedFor(nameof(DisplayRefreshRate))]
    [NotifyPropertyChangedFor(nameof(DisplayAdapterRam))]
    public partial HardwareInfoVideoController Snapshot { get; set; } = default!;

    public string Name => FirstNonEmpty(Snapshot.Name, Snapshot.Caption, Snapshot.Description) ?? "Unknown";

    public string Manufacturer => FirstNonEmpty(Snapshot.Manufacturer) ?? "Unknown";

    public string VideoProcessor => FirstNonEmpty(Snapshot.VideoProcessor) ?? "Unknown";

    public string DriverVersion => FirstNonEmpty(Snapshot.DriverVersion) ?? "Unknown";

    public string DisplayDriverDate => Snapshot.DisplayDriverDate;

    public string VideoModeDescription => FirstNonEmpty(Snapshot.VideoModeDescription) ?? "Unknown";

    public string DisplayResolution => Snapshot.DisplayResolution;

    public string DisplayRefreshRate => Snapshot.DisplayRefreshRate;

    public string DisplayAdapterRam => Snapshot.DisplayAdapterRam;

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
