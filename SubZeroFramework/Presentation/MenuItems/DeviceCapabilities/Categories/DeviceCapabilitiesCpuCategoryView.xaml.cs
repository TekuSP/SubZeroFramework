using System.Collections.Specialized;
using System.ComponentModel;

using SubZeroFramework.Controls.DeviceCapabilities.Models;
using SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

using Uno.Extensions.Navigation;

namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities.Categories;

/// <summary>
/// CPU category body: an instance picker on the left and a detail sub-region (<c>DetailHost</c>) resolved by
/// data navigation — picking an instance navigates the inner region with the live card model (DataViewMap).
/// Navigation is deferred to a later UI tick so it never runs re-entrantly during the outer region's own
/// navigation (same pattern as <c>FanDetailEditorView</c> / <c>DeviceCapabilitiesPage</c>).
/// </summary>
public sealed partial class DeviceCapabilitiesCpuCategoryView : UserControl, INotifyPropertyChanged
{
    private bool _syncQueued;
    private bool _itemsHooked;
    private int _navigatorRetries;
    private object? _lastNavigatedItem;

    public DeviceCapabilitiesCpuCategoryView()
    {
        this.InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is DeviceCapabilitiesCpuCategoryModel model)
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
    public DeviceCapabilitiesCpuCategoryModel ViewModel
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
        ((INotifyCollectionChanged)ViewModel.Page.CpuPackageCards).CollectionChanged += (_, _) => EnsureSelection();
    }

    /// <summary>Auto-selects the first instance once the collection has items; otherwise re-syncs the region.</summary>
    private void EnsureSelection()
    {
        if (ViewModel is null)
        {
            return;
        }

        if (Picker.SelectedItem is null && ViewModel.Page.CpuPackageCards.Count > 0)
        {
            // Triggers SelectionChanged, which queues the region sync.
            Picker.SelectedIndex = 0;
            return;
        }

        QueueSync();
    }

    private void OnPickerSelectionChanged(object sender, SelectionChangedEventArgs e) => QueueSync();

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
            SyncDetailRegion();
        });
    }

    private void SyncDetailRegion()
    {
        if (Picker.SelectedItem is not DeviceCapabilitiesCpuPackageCardModel item || ReferenceEquals(item, _lastNavigatedItem))
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
}
