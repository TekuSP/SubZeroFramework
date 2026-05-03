using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.WarningsIssues;

public partial class WarningIssuesModel : ObservableObject
{
    public WarningIssuesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
    }
}
