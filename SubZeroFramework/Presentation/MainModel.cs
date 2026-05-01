using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Interfaces;

namespace SubZeroFramework.Presentation;

public partial class MainModel : ObservableObject
{
    private INavigator _navigator;

    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        INavigator navigator,
        IFrameworkSystem frameworkSystem)
    {
        _navigator = navigator;
        Title = "Main";
        Title += $" - {localizer["ApplicationName"]}";
        Title += $" - {appInfo?.Value?.Environment}";
        Title += $" - {frameworkSystem.GetProductName()}";
    }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Name { get; set; }
}
