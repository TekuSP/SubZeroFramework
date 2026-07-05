using System.ComponentModel;

using SubZeroFramework.Controls.Settings.Models;
using SubZeroFramework.Controls.Settings.Models.Sections;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

/// <summary>
/// Settings page: a sub-navigation card on the left and a navigation sub-region (<c>SectionRegionHost</c>,
/// a ContentControl) on the right that resolves the per-section body views. The region is kept in sync with
/// the page model's <c>SelectedSectionIndex</c>, but the navigation is always deferred to a later UI tick so
/// it never runs re-entrantly during the page's own navigation (same pattern as
/// <c>DeviceCapabilitiesPage</c>).
/// </summary>
public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    private int _lastNavigatedIndex = -1;
    private bool _syncQueued;

    public SettingsPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
        Loaded += (_, _) => QueueSectionSync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Page exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation is required to push DataContext updates.")]
    public SettingsModel ViewModel
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
            QueueSectionSync();
        }
    } = default!;

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is SettingsModel model)
        {
            ViewModel = model;
        }
    }

    private void OnSectionItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SettingsSectionRailItemModel section)
        {
            ViewModel.SelectSectionCommand.Execute(section);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsModel.SelectedSectionIndex))
        {
            QueueSectionSync();
        }
    }

    // Coalesce + defer: navigating the child region during the page's own navigation deadlocks the UI thread.
    // Posting to the dispatcher runs the sync once, after the current navigation/load has unwound.
    private void QueueSectionSync()
    {
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _syncQueued = false;
            SyncSectionRegion();
        });
    }

    private void SyncSectionRegion()
    {
        if (ViewModel is null)
        {
            return;
        }

        var index = ViewModel.SelectedSectionIndex;
        if (index == _lastNavigatedIndex)
        {
            return;
        }

        if (SectionRegionHost.Navigator() is not { } navigator)
        {
            // Region not ready yet; a later Loaded/selection change re-queues.
            return;
        }

        _lastNavigatedIndex = index;
        _ = NavigateAsync(navigator, index);
    }

    private async Task NavigateAsync(INavigator navigator, int index)
    {
        // The section VMs bind to the page-driven model via SettingsAccessor (published in the page model's
        // ctor), so no navigation data is needed here.
        _ = index switch
        {
            1 => await navigator.NavigateViewModelAsync<SettingsUnitsSectionModel>(this),
            2 => await navigator.NavigateViewModelAsync<SettingsStartupSectionModel>(this),
            3 => await navigator.NavigateViewModelAsync<SettingsLicensesSectionModel>(this),
            4 => await navigator.NavigateViewModelAsync<SettingsAboutSectionModel>(this),
            _ => await navigator.NavigateViewModelAsync<SettingsServiceSectionModel>(this),
        };
    }
}
