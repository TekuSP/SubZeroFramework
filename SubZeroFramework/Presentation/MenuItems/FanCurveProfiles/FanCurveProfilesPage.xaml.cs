using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

public sealed partial class FanCurveProfilesPage : Page
{
    public FanCurveProfilesPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "<Pending>")]
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

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is FanCurveProfilesModel model)
        {
            ViewModel = model;
        }
    }
}
