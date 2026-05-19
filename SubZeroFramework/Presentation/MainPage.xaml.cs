using System.ComponentModel;

using Microsoft.UI.Windowing;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

using Windows.System;

namespace SubZeroFramework.Presentation;

public sealed partial class MainPage : Page, INotifyPropertyChanged
{

    public MainPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
        TitleBarHost.Loaded += TitleBarHost_Loaded;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "<Pending>")]
    public MainModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (string.Equals(args.SelectedItemContainer.Tag?.ToString(), "Github", StringComparison.OrdinalIgnoreCase))
        {
            _ = Launcher.LaunchUriAsync(new Uri("https://github.com/TekuSP/SubZeroFramework"));
            ViewModel!.navigator.GoBack(this);
        }
    }
}
