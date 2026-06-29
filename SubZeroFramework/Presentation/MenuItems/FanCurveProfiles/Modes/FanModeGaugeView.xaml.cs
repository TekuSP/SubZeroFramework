using System.ComponentModel;

using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles.Modes;

/// <summary>Shared mode gauge used by the Auto / Manual / Max bodies. Bound to a <see cref="FanModeModelBase"/>.</summary>
public sealed partial class FanModeGaugeView : UserControl, INotifyPropertyChanged
{
    public FanModeGaugeView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Lightweight UserControl ViewModel CLR property updates compiled x:Bind state without making ViewModel a dependency property.")]
    public FanModeModelBase ViewModel
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
