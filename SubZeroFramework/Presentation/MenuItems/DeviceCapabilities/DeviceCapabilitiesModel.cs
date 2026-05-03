using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

public partial class DeviceCapabilitiesModel : ObservableObject
{
    public DeviceCapabilitiesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
        
    }
}
