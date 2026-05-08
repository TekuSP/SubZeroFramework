using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

public partial class SettingsModel : ObservableObject
{
    [ObservableProperty]
    public partial string EndpointValidationMessage { get; set; }

    [ObservableProperty]
    public partial string LastStatusObservedAt { get; set; }

    public SettingsModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient)
    {
        EndpointValidationMessage = frameworkStatusClient.EndpointValidation.Message;
        LastStatusObservedAt = frameworkStatusClient.LastObservedAt is DateTimeOffset observedAt
            ? observedAt.LocalDateTime.ToString("T")
            : "No status received yet";
    }
}
