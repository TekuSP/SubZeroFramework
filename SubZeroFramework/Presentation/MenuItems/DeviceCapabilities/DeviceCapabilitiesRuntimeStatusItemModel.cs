using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

public partial class DeviceCapabilitiesRuntimeStatusItemModel : ObservableObject
{
    public DeviceCapabilitiesRuntimeStatusItemModel(string name, string status, Brush statusForegroundBrush)
    {
        Name = name;
        Status = status;
        StatusForegroundBrush = statusForegroundBrush;
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial Brush StatusForegroundBrush { get; set; }
}