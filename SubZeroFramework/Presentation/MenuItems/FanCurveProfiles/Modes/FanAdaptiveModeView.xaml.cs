using System.ComponentModel;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;
using SubZeroFramework.Models;

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
                DetachDialogHandlers();
                ViewModel = model;
                AttachDialogHandlers();

                // Attach as soon as the coordinator is assigned rather than only on Loaded, so a SelectedFan
                // set before this view loaded is not missed. Attach is idempotent.
                ViewModel.Attach();
            }
        };

        // A fan nothing is known about cannot run Adaptive, so it meets the consent dialog instead of an
        // editor full of controls for a loop that is not running. Raised from the view model rather than
        // polled here, so it also fires when the state arrives after the view is already up.
        DataContextChanged += (_, _) => ShowLockoutIfNeeded();

        Loaded += (_, _) =>
        {
            ShowLockoutIfNeeded();
            // Re-attached here as well as on DataContextChanged. Unloaded detaches them, and navigating away
            // and back reuses the same view and DataContext — so without this the buttons come back silently
            // dead, with no subscriber on the events they raise.
            AttachDialogHandlers();
            ViewModel?.Attach();
        };

        Unloaded += (_, _) =>
        {
            DetachDialogHandlers();
            ViewModel?.Detach();
        };
    }

    // Dialogs are hosted here rather than in the view model because a ContentDialog needs a XamlRoot, which
    // only a loaded view has. The view model raises an intent; this decides where to put it on screen.

    private void AttachDialogHandlers()
    {
        if (ViewModel is null)
        {
            return;
        }

        // Removed first so re-attaching cannot double-subscribe and open two dialogs for one click.
        ViewModel.CalibrationRequested -= OnCalibrationRequested;
        ViewModel.ExplainerRequested -= OnExplainerRequested;

        ViewModel.CalibrationRequested += OnCalibrationRequested;
        ViewModel.ExplainerRequested += OnExplainerRequested;
    }

    private void DetachDialogHandlers()
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.CalibrationRequested -= OnCalibrationRequested;
        ViewModel.ExplainerRequested -= OnExplainerRequested;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// True while a dialog is open, so a second click cannot try to open another.
    /// </summary>
    /// <remarks>
    /// The commands complete the instant they raise their event, so the button is live again immediately.
    /// WinUI permits exactly one ContentDialog at a time, and the second ShowAsync throws — from an
    /// <c>async void</c> handler that is an unhandled exception on the UI thread, i.e. the app closing.
    /// </remarks>
    private bool _dialogOpen;

    /// <summary>
    /// Opens the consent dialog when this fan has nothing measured and nothing learned.
    /// </summary>
    /// <remarks>
    /// The store refuses to arm such a fan, so without this the mode simply fails to stick with nothing on
    /// screen explaining why. Offered once per view activation rather than on every state change — a dialog
    /// that reappears the moment it is dismissed is a trap, and the same test is reachable from two buttons
    /// in the editor for anyone who changes their mind.
    /// </remarks>
    private void ShowLockoutIfNeeded()
    {
        if (_lockoutOffered || ViewModel is not { IsAwaitingFirstLearning: true })
        {
            return;
        }

        _lockoutOffered = true;
        OnCalibrationRequested(this, EventArgs.Empty);
    }

    private bool _lockoutOffered;

    private async void OnCalibrationRequested(object? sender, EventArgs e)
    {
        if (_dialogOpen || XamlRoot is null || ViewModel?.SelectedFan is not { } fan)
        {
            return;
        }

        var model = new FanCalibrationDialogModel(
            fan.Snapshot.DisplayName,
            ViewModel.CoolingRole,
            ViewModel.UnitFormattingService);

        using var dialog = new FanCalibrationDialog(model) { XamlRoot = XamlRoot };

        // The dialog stays open across the whole run, so consent must not close it: the primary button is
        // intercepted, the deferral held, and the same dialog re-used for the live run and the outcome.
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (model.Stage != FanCalibrationStage.Consent)
            {
                // The outcome's "Done" — let it close.
                return;
            }

            args.Cancel = true;
            var deferral = args.GetDeferral();

            try
            {
                model.BeginRun();

                var result = await ViewModel.StartCalibrationAsync(
                    fan.Snapshot.FanIndex,
                    [.. ViewModel.DrivingSensorIndices],
                    new Progress<FanCalibrationProgress>(model.Apply),
                    dialog.CancellationToken);

                model.Complete(result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed CALL, as opposed to a failed run. Reported in the same place rather than thrown at
                // a user who is looking at a progress bar.
                model.Complete(new FanCalibrationRunResult
                {
                    FanIndex = fan.Snapshot.FanIndex,
                    Succeeded = false,
                    Failure = FanCalibrationFailure.ClientDisconnected,
                    FansRestored = false,
                });
            }
            finally
            {
                deferral.Complete();
            }
        };

        _dialogOpen = true;

        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            // An async void handler that lets anything escape takes the app with it. The run itself is
            // already reported inside the dialog; this catches failures of the dialog machinery.
            System.Diagnostics.Debug.WriteLine($"The calibration dialog could not be shown: {exception}");
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void OnExplainerRequested(object? sender, EventArgs e)
    {
        if (_dialogOpen || XamlRoot is null || ViewModel?.SelectedFan is not { } fan)
        {
            return;
        }

        _dialogOpen = true;

        try
        {
            // Snapshotted here rather than bound: the page keeps ticking behind the dialog, and a reference
            // whose numbers move while it is being read is worse than one that is a few seconds old.
            var model = new FanControlExplainerModel(
                fan,
                fan.ControlState?.Calibration,
                fan.ControlState?.AdaptiveSettings,
                fan.ControlState?.AdaptiveControl,
                ViewModel.CoolingRole,
                ViewModel.UnitFormattingService);

            await new FanControlExplainerDialog(model) { XamlRoot = XamlRoot }.ShowAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"The explainer dialog could not be shown: {exception}");
        }
        finally
        {
            _dialogOpen = false;
        }
    }

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
