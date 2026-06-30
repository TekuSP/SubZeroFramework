using System.ComponentModel;

using SubZeroFramework.Controls.Thermal.Models;

namespace SubZeroFramework.Controls.Thermal;

public sealed partial class ThermalSensorTileView : UserControl, INotifyPropertyChanged
{
    public ThermalSensorTileView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Lightweight UserControl ViewModel CLR property updates compiled x:Bind state without making ViewModel a dependency property.")]
    public ThermalSensorModel ViewModel
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
