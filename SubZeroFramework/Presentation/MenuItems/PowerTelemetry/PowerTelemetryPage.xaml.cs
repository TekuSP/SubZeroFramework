using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

public sealed partial class PowerTelemetryPage : Page, INotifyPropertyChanged
{
    public PowerTelemetryPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "<Pending>")]
    public PowerTelemetryModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is PowerTelemetryModel model)
        {
            ViewModel = model;
        }
    }
}
