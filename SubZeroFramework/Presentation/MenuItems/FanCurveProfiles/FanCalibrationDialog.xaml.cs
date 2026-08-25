using SubZeroFramework.Controls.FanCurveProfiles.Models;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// The calibration wizard, and the gate a fan nothing is known about meets instead of the Adaptive editor.
/// </summary>
/// <remarks>
/// <para>
/// A dialog rather than a page, because a calibration is a modal commitment: it takes minutes, deliberately
/// heats the machine, and drives the fan to both extremes. Wandering off mid-run and forgetting it is
/// happening is a worse outcome than being briefly interrupted.
/// </para>
/// <para>
/// <b>Closing is cancelling.</b> The client's stream IS the run's lease — ending the call aborts the test in
/// the service, stops the load, and returns the fan to the exact mode it had. That guarantee lives in the
/// service rather than here, so it also holds if the app is killed outright.
/// </para>
/// </remarks>
public sealed partial class FanCalibrationDialog : ContentDialog, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    public FanCalibrationDialog(FanCalibrationDialogModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        this.InitializeComponent();

        // Wired here rather than declared in XAML because cancelling is a PHYSICAL act — it stops the load and
        // hands the fan back — not a property anyone should be able to set to something inconsistent.
        Closing += OnClosing;
    }

    public FanCalibrationDialogModel ViewModel { get; }

    /// <summary>Cancels the run. The service restores the fan whether or not anything here is still listening.</summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    public void Dispose()
    {
        Closing -= OnClosing;
        _cancellation.Dispose();
    }

    /// <summary>
    /// Stops a running test, and keeps the dialog open long enough to say what happened.
    /// </summary>
    /// <remarks>
    /// Closing immediately would cancel the run and vanish, so the outcome screen for a stopped test — which
    /// is the one that confirms the fan was handed back — could never be seen. The close is vetoed once while
    /// running; the run then completes as Cancelled and the outcome path closes the dialog normally.
    /// </remarks>
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.Stage != FanCalibrationStage.Running)
        {
            _cancellation.Cancel();
            return;
        }

        args.Cancel = true;
        _cancellation.Cancel();
    }
}
