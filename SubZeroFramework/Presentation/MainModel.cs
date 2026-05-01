using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation;

public partial class MainModel : ObservableObject
{
    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
        Title = $"Main - {localizer["ApplicationName"]} - {appInfo?.Value?.Environment}";
    }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Name { get; set; }
}
