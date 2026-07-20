using System.ComponentModel;

using SubZeroFramework.Controls.Settings.Models;
using SubZeroFramework.Presentation.MenuItems.Settings.Sections;
using SubZeroFramework.Services.Navigation;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Presentation.MenuItems.Settings;

/// <summary>
/// Settings page: a sub-navigation card on the left and a navigation sub-region (<c>SectionRegionHost</c>,
/// a ContentControl) on the right that resolves the per-section body views with their ViewMap-registered
/// ViewModels. The region is kept in sync with the page model's <c>SelectedSectionIndex</c>, but the
/// navigation is always deferred to a later UI tick so it never runs re-entrantly during the page's own
/// navigation (same pattern as <c>DeviceCapabilitiesPage</c>).
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
                // Register the shell guard: leaving the Settings tab warns when the ACTIVE section is dirty.
                field.GuardRegistry.Register("Settings", CurrentSectionGuard);
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
            QueueSectionSync();
        }
    } = default!;

    /// <summary>The unsaved-changes guard of whichever section body is currently shown (null when clean-only).</summary>
    private IUnsavedChangesGuard? CurrentSectionGuard()
        => (SectionRegionHost.Content as FrameworkElement)?.DataContext as IUnsavedChangesGuard;

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is SettingsModel model)
        {
            ViewModel = model;
        }
    }

    private async void OnSectionItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SettingsSectionRailItemModel section)
        {
            return;
        }

        // Re-picking the current section is a no-op; only a real switch can lose staged edits.
        if (section.Index != ViewModel.SelectedSectionIndex
            && CurrentSectionGuard() is { HasUnsavedChanges: true } guard
            && XamlRoot is { } xamlRoot)
        {
            if (!await UnsavedChangesPrompt.ConfirmDiscardAsync(xamlRoot))
            {
                return; // Stay on the current section.
            }

            await guard.DiscardUnsavedChangesAsync();
        }

        ViewModel.SelectSectionCommand.Execute(section);
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
        // Navigation constructs each section ViewModel fresh (ViewMap-registered) but never disposes the
        // one it replaces, so the outgoing VM's stream subscriptions are released here.
        var previousViewModel = (SectionRegionHost.Content as FrameworkElement)?.DataContext as IDisposable;

        var response = index switch
        {
            1 => await navigator.NavigateViewModelAsync<SettingsUnitsSectionModel>(this),
            2 => await navigator.NavigateViewModelAsync<SettingsStartupSectionModel>(this),
            3 => await navigator.NavigateViewModelAsync<SettingsLicensesSectionModel>(this),
            4 => await navigator.NavigateViewModelAsync<SettingsAboutSectionModel>(this),
            _ => await navigator.NavigateViewModelAsync<SettingsServiceSectionModel>(this),
        };

        if (response is not null)
        {
            previousViewModel?.Dispose();
        }
    }
}
