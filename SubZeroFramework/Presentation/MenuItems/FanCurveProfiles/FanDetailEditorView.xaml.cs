using System;
using System.ComponentModel;
using System.Threading.Tasks;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

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

    public FanDetailEditorView()
    {
        this.InitializeComponent();
        Loaded += (_, _) => QueueModeSync();
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
        if (ViewModel is null)
        {
            return;
        }

        var index = ViewModel.SelectedModeIndex;
        if (index == _lastNavigatedIndex)
        {
            return;
        }

        if (ModeRegionHost.Navigator() is not { } navigator)
        {
            // Region not ready yet; a later poll/Loaded re-queues.
            return;
        }

        _lastNavigatedIndex = index;
        _ = NavigateAsync(navigator, index);
    }

    private async Task NavigateAsync(INavigator navigator, int index)
    {
        // The mode VMs bind to the page-driven coordinator via FanCoordinatorAccessor (published in the
        // coordinator's ctor), so no navigation data is needed here.
        _ = index switch
        {
            1 => await navigator.NavigateViewModelAsync<FanManualModeModel>(this),
            2 => await navigator.NavigateViewModelAsync<FanCustomCurveModel>(this),
            3 => await navigator.NavigateViewModelAsync<FanMaxModeModel>(this),
            _ => await navigator.NavigateViewModelAsync<FanAutoModeModel>(this),
        };
    }
}
