using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Presentation;

public partial class MainModel : ObservableObject
{
    public MainModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo, IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    [ObservableProperty]
    public partial IServiceProvider ServiceProvider { get; set; }
}
