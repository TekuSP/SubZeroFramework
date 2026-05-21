using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities;

public sealed partial class NetworkSectionView : UserControl, INotifyPropertyChanged
{
    public NetworkSectionView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Lightweight UserControl ViewModel CLR property updates compiled x:Bind state without making ViewModel a dependency property.")]
    public DeviceCapabilitiesNetworkSectionModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;
}
