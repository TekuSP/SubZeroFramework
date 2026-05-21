using System.ComponentModel;

using SubZeroFramework.Controls.Power.Models;

namespace SubZeroFramework.Controls.Power;

public sealed partial class PowerCardView : UserControl, INotifyPropertyChanged
{
    public PowerCardView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Lightweight UserControl ViewModel CLR property updates compiled x:Bind state without making ViewModel a dependency property.")]
    public PowerCardModel ViewModel
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
