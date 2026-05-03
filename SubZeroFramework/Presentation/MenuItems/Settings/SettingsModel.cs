using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

public partial class SettingsModel : ObservableObject
{
    public SettingsModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
    }
}
