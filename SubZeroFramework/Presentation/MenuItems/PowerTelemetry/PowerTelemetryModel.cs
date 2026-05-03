using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

public partial class PowerTelemetryModel : ObservableObject
{
    public PowerTelemetryModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
    }
}
