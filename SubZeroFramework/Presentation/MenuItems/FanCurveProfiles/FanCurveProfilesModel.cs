using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

public partial class FanCurveProfilesModel : ObservableObject
{
    public FanCurveProfilesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
    }
}
