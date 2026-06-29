using System.ComponentModel;

using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles.Modes;

/// <summary>Custom curve mode body, resolved by the mode navigation sub-region. DataContext is the <see cref="FanCustomCurveModel"/>.</summary>
public sealed partial class FanCustomCurveView : UserControl, INotifyPropertyChanged
{
    public FanCustomCurveView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is FanCustomCurveModel model)
            {
                ViewModel = model;
                // Attach as soon as the coordinator is assigned (not only on Loaded) so the gauge/description
                // projections never miss a SelectedFan that was set before this view loaded. Attach is idempotent.
                ViewModel.Attach();
            }
        };
        Loaded += (_, _) => ViewModel?.Attach();
        Unloaded += (_, _) => ViewModel?.Detach();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public FanCustomCurveModel ViewModel
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
