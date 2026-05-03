using Microsoft.UI.Windowing;

namespace SubZeroFramework.Presentation;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
        DraggableTitleBar.Loaded += TitleBarHost_Loaded;
    }

    private void TitleBarHost_Loaded(object sender, RoutedEventArgs e)
    {
        DraggableTitleBar.Loaded -= TitleBarHost_Loaded;

        if (Application.Current is App app && app.MainWindow is not null && AppWindowTitleBar.IsCustomizationSupported())
        {
            app.MainWindow.SetTitleBar(DraggableTitleBar);
        }
    }

    private void PaneToggleButton_Click(object sender, RoutedEventArgs e)
    {
        MainNavigationView.IsPaneOpen = !MainNavigationView.IsPaneOpen;
    }
}
