using System.Collections.Specialized;
using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models;
using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;

/// <summary>
/// Graphics category body: adapter and monitor instance pickers on the left and two stacked detail sub-regions
/// (<c>DetailHost</c> / <c>MonitorDetailHost</c>) resolved by data navigation — picking an instance navigates the
/// matching inner region with the live card model (DataViewMap). Navigation is deferred to a later UI tick so it
/// never runs re-entrantly during the outer region's own navigation (same pattern as <c>FanDetailEditorView</c> /
/// <c>DeviceCapabilitiesPage</c>).
/// </summary>
public sealed partial class DeviceCapabilitiesGraphicsCategoryView : UserControl, INotifyPropertyChanged
{
    private bool _syncQueued;
    private bool _itemsHooked;
    private int _navigatorRetries;
    private int _monitorNavigatorRetries;
    private object? _lastNavigatedItem;
    private object? _lastNavigatedMonitor;

    public DeviceCapabilitiesGraphicsCategoryView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is DeviceCapabilitiesGraphicsCategoryModel model)
            {
                ViewModel = model;
                HookItems();
                EnsureSelection();
            }
        };
        Loaded += (_, _) => EnsureSelection();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("SubZeroFramework.Mvvm", "SZF0009:Avoid direct PropertyChanged event invocation", Justification = "Navigation sets DataContext; the CLR ViewModel property feeds compiled x:Bind without a dependency property.")]
    public DeviceCapabilitiesGraphicsCategoryModel ViewModel
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
        }
    } = default!;

    private void HookItems()
    {
        if (_itemsHooked)
        {
            return;
        }

        _itemsHooked = true;
        ((INotifyCollectionChanged)ViewModel.Page.GraphicsCardGroups).CollectionChanged += (_, _) => EnsureSelection();
        ((INotifyCollectionChanged)ViewModel.Page.MonitorCards).CollectionChanged += (_, _) => EnsureSelection();
    }

    /// <summary>Auto-selects the first instance in each picker once its collection has items; otherwise re-syncs the regions.</summary>
    private void EnsureSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (Picker.SelectedItem is null && ViewModel.Page.GraphicsCardGroups.Count > 0)
        {
            // Triggers SelectionChanged, which queues the region sync.
            Picker.SelectedIndex = 0;
        }

        if (MonitorPicker.SelectedItem is null && ViewModel.Page.MonitorCards.Count > 0)
        {
            MonitorPicker.SelectedIndex = 0;
        }

        QueueSync();
    }

    private void OnPickerSelectionChanged(object sender, SelectionChangedEventArgs e) => QueueSync();

    private void OnMonitorPickerSelectionChanged(object sender, SelectionChangedEventArgs e) => QueueSync();

    private void QueueSync()
    {
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _syncQueued = false;
            SyncAdapterDetailRegion();
            SyncMonitorDetailRegion();
        });
    }

    private void SyncAdapterDetailRegion()
    {
        if (Picker.SelectedItem is not DeviceCapabilitiesGraphicsCardGroupModel item || ReferenceEquals(item, _lastNavigatedItem))
        {
            return;
        }

        if (DetailHost.Navigator() is not { } navigator)
        {
            // Region not attached yet (this view itself was just navigated in); retry a few ticks.
            if (_navigatorRetries++ < 20)
            {
                QueueSync();
            }

            return;
        }

        _navigatorRetries = 0;
        _lastNavigatedItem = item;
        _ = navigator.NavigateDataAsync(this, item);
    }

    private void SyncMonitorDetailRegion()
    {
        if (MonitorPicker.SelectedItem is not DeviceCapabilitiesMonitorCardModel monitor || ReferenceEquals(monitor, _lastNavigatedMonitor))
        {
            return;
        }

        if (MonitorDetailHost.Navigator() is not { } navigator)
        {
            if (_monitorNavigatorRetries++ < 20)
            {
                QueueSync();
            }

            return;
        }

        _monitorNavigatorRetries = 0;
        _lastNavigatedMonitor = monitor;
        _ = navigator.NavigateDataAsync(this, monitor);
    }
}
