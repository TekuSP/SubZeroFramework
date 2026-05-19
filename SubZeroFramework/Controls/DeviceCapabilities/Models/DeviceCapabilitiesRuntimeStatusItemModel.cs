using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public partial class DeviceCapabilitiesRuntimeStatusItemModel : ObservableObject
{
    public DeviceCapabilitiesRuntimeStatusItemModel(string name, string status, DeviceCapabilitiesStatusTone statusTone)
    {
        Name = name;
        Status = status;
        StatusTone = statusTone;
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial DeviceCapabilitiesStatusTone StatusTone { get; set; }
}
