using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities;

public sealed partial class CoolingSectionView : UserControl, INotifyPropertyChanged
{
    public CoolingSectionView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "<Pending>")]
    public DeviceCapabilitiesCoolingSectionModel ViewModel
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
