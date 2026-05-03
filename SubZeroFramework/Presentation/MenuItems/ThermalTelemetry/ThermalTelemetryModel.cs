using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.ThermalTelemetry;

public partial class ThermalTelemetryModel : ObservableObject
{
    public ThermalTelemetryModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
    }
}
