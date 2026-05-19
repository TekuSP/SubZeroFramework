using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

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
