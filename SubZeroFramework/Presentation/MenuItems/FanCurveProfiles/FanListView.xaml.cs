using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Master pane of the Fan Control page: the "Fans by location" list plus the always-visible commit
/// footer. Purely presentational — all state and commands live on the shared <see cref="FanCurveProfilesModel"/>
/// supplied by the host page via <see cref="ViewModel"/>.
/// </summary>
public sealed partial class FanListView : UserControl, INotifyPropertyChanged
{
    public FanListView()
    {
        this.InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "UserControl exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation pushes the host-supplied ViewModel into the bindings.")]
    public FanCurveProfilesModel ViewModel
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
