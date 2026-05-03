using System.ComponentModel;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class DeviceCapabilitiesPage : Page, INotifyPropertyChanged
{
    public DeviceCapabilitiesPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DeviceCapabilitiesModel? ViewModel
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
        if (args.NewValue is DeviceCapabilitiesModel model)
        {
            ViewModel = model;
        }
    }
}
