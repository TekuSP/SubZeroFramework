using System;
using System.ComponentModel;
using System.Threading.Tasks;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;
using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles.Modes;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Detail pane of the Fan Control page: the selected fan's header, "Applies to" link card, mode selector,
/// and the sticky action bar. The mode body is a navigation sub-region (<c>ModeRegionHost</c>, a
/// ContentControl) that resolves the Auto / Manual / Max / Custom views. The region is kept in sync with the
/// coordinator's effective mode, but the navigation is always deferred to a later UI tick so it never runs
/// re-entrantly during the page's own navigation (which deadlocks the UI thread).
/// </summary>
public sealed partial class FanDetailEditorView : UserControl, INotifyPropertyChanged
{
    private int _lastNavigatedIndex = -1;
    private bool _syncQueued;
    private bool _navigating;

    public FanDetailEditorView()
    {
        this.InitializeComponent();
        Loaded += (_, _) => QueueModeSync();

        // The mode body's navigator does not exist until its own host is loaded and the region has been
        // attached to the navigation tree. Waiting for THAT event is the reliable trigger; polling for it
        // instead asks an unattached region for its navigator over and over, which logs
        // "Unable to find service provider for root navigator" on every attempt.
        ModeRegionHost.Loaded += (_, _) => QueueModeSync();

        // Detaching discards whatever the region was showing, and re-attaching puts it back on its DEFAULT
        // route — which is Auto. Anything we remember navigating to before that point describes a view that
        // no longer exists, so forget it here or the guard in SyncModeRegion will suppress the very
        // navigation that would put the body back on the selected mode.
        ModeRegionHost.Unloaded += (_, _) => _lastNavigatedIndex = -1;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "UserControl exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation pushes the host-supplied ViewModel into the bindings.")]
    public FanCurveProfilesModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            if (field is not null)
            {
                field.PropertyChanged -= OnViewModelPropertyChanged;
            }

            field = value;

            if (field is not null)
            {
                field.PropertyChanged += OnViewModelPropertyChanged;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
            QueueModeSync();
        }
    } = default!;

    private void LinkChip_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.FrameworkElement { DataContext: FanLinkChip chip })
        {
            ViewModel.LinkSection.ToggleLinkCommand.Execute(chip);
        }
    }

    private void ModeSegment_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Deselection is prevented by SegmentedSelection.KeepsSelection on the buttons themselves, so this
        // only has to record the choice.
        if (sender is Microsoft.UI.Xaml.FrameworkElement { Tag: string tag }
            && int.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out var index))
        {
            ViewModel.SelectedModeIndex = index;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FanCurveProfilesModel.SelectedModeIndex))
        {
            QueueModeSync();
        }
    }

    // Coalesce + defer: SelectedModeIndex is re-raised on every telemetry poll, and navigating the child
    // region during the page's own navigation deadlocks. Posting to the dispatcher runs the sync once, after
    // the current navigation/load has unwound.
    private void QueueModeSync()
    {
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _syncQueued = false;
            SyncModeRegion();
        });
    }

    private void SyncModeRegion()
    {
        if (ViewModel is null || _navigating)
        {
            return;
        }

        var index = ViewModel.SelectedModeIndex;
        if (index == _lastNavigatedIndex && !ShowsWrongModeView(index))
        {
            return;
        }

        if (ModeRegionHost.Navigator() is not { } navigator)
        {
            // Not ready yet. Do NOT poll: ModeRegionHost.Loaded re-queues this once the region is attached,
            // which is what fixes the "opened on Auto for a curve-driven fan" case without asking an
            // unattached region for a navigator on every UI tick.
            _lastNavigatedIndex = -1;
            return;
        }

        _ = NavigateAsync(navigator, index);
    }

    private async Task NavigateAsync(INavigator navigator, int index)
    {
        _navigating = true;
        try
        {
            // The mode VMs bind to the page-driven coordinator via FanCoordinatorAccessor (published in the
            // coordinator's ctor), so no navigation data is needed here.
            _ = index switch
            {
                1 => await navigator.NavigateViewModelAsync<FanManualModeModel>(this),
                2 => await navigator.NavigateViewModelAsync<FanCustomCurveModel>(this),
                3 => await navigator.NavigateViewModelAsync<FanMaxModeModel>(this),
                4 => await navigator.NavigateViewModelAsync<FanAdaptiveModeModel>(this),
                _ => await navigator.NavigateViewModelAsync<FanAutoModeModel>(this),
            };
        }
        finally
        {
            _navigating = false;
        }

        // Recorded AFTER the await, and only if it took. This used to be assigned before navigating, which
        // made it a record of what was ASKED FOR — and nothing ever cleared it, so one navigation that did
        // not land desynchronised the body from the mode selector for the life of this view.
        _lastNavigatedIndex = ShowsWrongModeView(index) ? -1 : index;

        // The mode can move while this awaits (telemetry, or a profile being applied).
        if (ViewModel is not null && ViewModel.SelectedModeIndex != _lastNavigatedIndex)
        {
            QueueModeSync();
        }
    }

    /// <summary>
    /// True only when the region is DEMONSTRABLY showing the wrong mode — its content is one of the mode
    /// views and it is not the one <paramref name="index"/> asks for.
    /// </summary>
    /// <remarks>
    /// Deliberately one-sided. Content the region hosts in some wrapper is not something this view models, so
    /// it is left alone rather than treated as a mismatch that would re-navigate on every telemetry tick.
    /// </remarks>
    private bool ShowsWrongModeView(int index) => ModeRegionHost.Content is
        FanAutoModeView or FanManualModeView or FanCustomCurveView or FanMaxModeView or FanAdaptiveModeView
        && ModeRegionHost.Content.GetType() != ModeViewTypeFor(index);

    private static Type ModeViewTypeFor(int index) => index switch
    {
        1 => typeof(FanManualModeView),
        2 => typeof(FanCustomCurveView),
        3 => typeof(FanMaxModeView),
        4 => typeof(FanAdaptiveModeView),
        _ => typeof(FanAutoModeView),
    };
}
