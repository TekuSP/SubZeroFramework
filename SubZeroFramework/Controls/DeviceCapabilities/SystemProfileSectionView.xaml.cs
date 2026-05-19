using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models;

namespace SubZeroFramework.Controls.DeviceCapabilities;

public sealed partial class SystemProfileSectionView : UserControl, INotifyPropertyChanged
{
    public SystemProfileSectionView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "<Pending>")]
    public DeviceCapabilitiesSystemProfileSectionModel ViewModel
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