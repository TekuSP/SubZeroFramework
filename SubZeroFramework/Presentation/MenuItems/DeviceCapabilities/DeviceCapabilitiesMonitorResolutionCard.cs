using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

public partial class DeviceCapabilitiesMonitorResolutionCard : ObservableObject
{
    public DeviceCapabilitiesMonitorResolutionCard(string title, string resolutionTier)
    {
        Title = title;
        ResolutionTier = resolutionTier;
    }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string ResolutionTier { get; set; }
}