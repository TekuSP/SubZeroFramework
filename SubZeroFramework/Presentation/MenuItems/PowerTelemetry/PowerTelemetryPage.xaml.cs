using System.ComponentModel;

namespace SubZeroFramework.Presentation.MenuItems.PowerTelemetry;

public sealed partial class PowerTelemetryPage : Page, INotifyPropertyChanged
{
    public PowerTelemetryPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Page exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation is required to push DataContext updates.")]
    public PowerTelemetryModel ViewModel
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
        if (args.NewValue is PowerTelemetryModel model)
        {
            ViewModel = model;
        }
    }

    /// <summary>Opening the section is the request to read the pack.</summary>
    private void PackHealthExpander_Expanding(Microsoft.UI.Xaml.Controls.Expander sender, Microsoft.UI.Xaml.Controls.ExpanderExpandingEventArgs args)
        => ViewModel?.OnPackHealthExpanded();

    /// <summary>
    /// Pull-to-refresh: re-reads the pack.
    /// </summary>
    /// <remarks>
    /// The deferral is held until the read finishes, which is what keeps the refresh visualizer spinning for
    /// the duration rather than snapping back the instant the gesture ends.
    /// </remarks>
    private async void PackHealthRefresh_Requested(Microsoft.UI.Xaml.Controls.RefreshContainer sender, Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();

        if (ViewModel?.RefreshPackHealthCommand is { } command)
        {
            await command.ExecuteAsync(parameter: null);
        }
    }
}
