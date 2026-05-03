using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.Dashboard;

public partial class DashboardModel : ObservableObject
{
    public DashboardModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
        
    }
}
