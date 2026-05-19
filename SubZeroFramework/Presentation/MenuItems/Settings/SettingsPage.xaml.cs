using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    public SettingsPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "<Pending>")]
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
}
