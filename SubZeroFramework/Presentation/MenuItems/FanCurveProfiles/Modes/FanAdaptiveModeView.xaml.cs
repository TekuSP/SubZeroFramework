using System.ComponentModel;

using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles.Modes;

/// <summary>
/// Adaptive mode body, resolved by the mode navigation sub-region. DataContext is the
/// <see cref="FanAdaptiveModeModel"/>.
/// </summary>
public sealed partial class FanAdaptiveModeView : UserControl, INotifyPropertyChanged
{
    public FanAdaptiveModeView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is FanAdaptiveModeModel model)
            {
                ViewModel = model;

                // Attach as soon as the coordinator is assigned rather than only on Loaded, so a SelectedFan
                // set before this view loaded is not missed. Attach is idempotent.
                ViewModel.Attach();
            }
        };

        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) => ViewModel?.Detach();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public FanAdaptiveModeModel ViewModel
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
