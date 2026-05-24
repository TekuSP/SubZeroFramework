using System.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Storage.Pickers;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    public SettingsPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Page exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation is required to push DataContext updates.")]
    public SettingsModel ViewModel
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
        if (args.NewValue is SettingsModel model)
        {
            ViewModel = model;
        }
    }

    private async void OnChangeConfigurationPathClick(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync().ConfigureAwait(true);
        if (folder is null)
        {
            return;
        }

        await ViewModel.RelocateConfigurationStoreCommand.ExecuteAsync(folder);
    }

    private async void OnChangeUserPreferencesPathClick(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync().ConfigureAwait(true);
        if (folder is null)
        {
            return;
        }

        await ViewModel.RelocateUnitPreferencesStoreCommand.ExecuteAsync(folder);
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");

        if (Application.Current is App app && app.MainWindow is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(app.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
