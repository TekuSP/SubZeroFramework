using System.ComponentModel;

using Microsoft.UI.Windowing;

namespace SubZeroFramework.Presentation;

public sealed partial class MainPage : Page, INotifyPropertyChanged
{
    public MainPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
        TitleBarHost.Loaded += TitleBarHost_Loaded;
    }


    public event PropertyChangedEventHandler? PropertyChanged;
    public MainModel? ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    }

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is MainModel model)
        {
            ViewModel = model;
        }
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
