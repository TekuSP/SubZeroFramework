using Microsoft.UI.Windowing;

namespace SubZeroFramework.Presentation;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
        TitleBarHost.Loaded += TitleBarHost_Loaded;
    }

    private void TitleBarHost_Loaded(object sender, RoutedEventArgs e)
    {
        TitleBarHost.Loaded -= TitleBarHost_Loaded;

        if (Application.Current is App app && app.MainWindow is not null && AppWindowTitleBar.IsCustomizationSupported())
        {
            app.MainWindow.SetTitleBar(TitleBarHost);
        }
        else if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            TitleBarHost.Visibility = Visibility.Collapsed;
            MainNavigationView.IsPaneToggleButtonVisible = true;
        }
    }

    private void PaneToggleButton_Click(object sender, RoutedEventArgs e)
    {
        MainNavigationView.IsPaneOpen = !MainNavigationView.IsPaneOpen;
    }
}
