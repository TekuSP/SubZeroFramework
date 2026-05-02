namespace SubZeroFramework.Presentation;

public sealed partial class MainPage : Page
{
    public MainPage()
    {   
        this.InitializeComponent();
        this.Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;

        if (Application.Current is App app && app.MainWindow is not null)
        {
            app.MainWindow.SetTitleBar(TitleBarHost);
        }
    }

    private void PaneToggleButton_Click(object sender, RoutedEventArgs e)
    {
        MainNavigationView.IsPaneOpen = !MainNavigationView.IsPaneOpen;
    }
}
