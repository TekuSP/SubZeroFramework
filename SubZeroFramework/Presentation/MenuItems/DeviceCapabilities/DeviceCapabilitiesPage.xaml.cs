using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models;
using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

/// <summary>
/// Device Capabilities page: a category rail on the left and a navigation sub-region (<c>CategoryRegionHost</c>,
/// a ContentControl) on the right that resolves the per-category body views. The region is kept in sync with the
/// page model's <c>SelectedCategoryIndex</c>, but the navigation is always deferred to a later UI tick so it
/// never runs re-entrantly during the page's own navigation (same pattern as <c>FanDetailEditorView</c>).
/// </summary>
public sealed partial class DeviceCapabilitiesPage : Page, INotifyPropertyChanged
{
    private int _lastNavigatedIndex = -1;
    private bool _syncQueued;

    public DeviceCapabilitiesPage()
    {
        this.InitializeComponent();
        DataContextChanged += DataContextChanged_Handler;
        Loaded += (_, _) => QueueCategorySync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Page exposes ViewModel as a CLR property (not a DependencyProperty) to support compiled x:Bind; direct PropertyChanged invocation is required to push DataContext updates.")]
    public DeviceCapabilitiesModel ViewModel
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
            QueueCategorySync();
        }
    } = default!;

    private void DataContextChanged_Handler(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is DeviceCapabilitiesModel model)
        {
            ViewModel = model;
        }
    }

    private void OnCategoryItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceCapabilitiesCategoryRailItemModel category)
        {
            ViewModel.SelectCategoryCommand.Execute(category);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceCapabilitiesModel.SelectedCategoryIndex))
        {
            QueueCategorySync();
        }
    }

    // Coalesce + defer: navigating the child region during the page's own navigation deadlocks the UI thread.
    // Posting to the dispatcher runs the sync once, after the current navigation/load has unwound.
    private void QueueCategorySync()
    {
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _syncQueued = false;
            SyncCategoryRegion();
        });
    }

    private void SyncCategoryRegion()
    {
        if (ViewModel is null)
        {
            return;
        }

        var index = ViewModel.SelectedCategoryIndex;
        if (index == _lastNavigatedIndex)
        {
            return;
        }

        if (CategoryRegionHost.Navigator() is not { } navigator)
        {
            // Region not ready yet; a later Loaded/selection change re-queues.
            return;
        }

        _lastNavigatedIndex = index;
        _ = NavigateAsync(navigator, index);
    }

    private async Task NavigateAsync(INavigator navigator, int index)
    {
        // The category VMs bind to the page-driven model via DeviceCapabilitiesAccessor (published in the
        // page model's ctor), so no navigation data is needed here.
        _ = index switch
        {
            1 => await navigator.NavigateViewModelAsync<DeviceCapabilitiesCpuCategoryModel>(this),
            2 => await navigator.NavigateViewModelAsync<DeviceCapabilitiesMemoryCategoryModel>(this),
            3 => await navigator.NavigateViewModelAsync<DeviceCapabilitiesStorageCategoryModel>(this),
            4 => await navigator.NavigateViewModelAsync<DeviceCapabilitiesGraphicsCategoryModel>(this),
            5 => await navigator.NavigateViewModelAsync<DeviceCapabilitiesNetworkCategoryModel>(this),
            6 => await navigator.NavigateViewModelAsync<DeviceCapabilitiesSystemProfileCategoryModel>(this),
            _ => await navigator.NavigateViewModelAsync<DeviceCapabilitiesOnboardCategoryModel>(this),
        };
    }
}
