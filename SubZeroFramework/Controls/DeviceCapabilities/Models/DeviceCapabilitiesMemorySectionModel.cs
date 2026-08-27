using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// The Memory section's slice over the Device Capabilities page model. Every figure it shows is MIRRORED as
/// a stored property that <see cref="RefreshDerivedState"/> reassigns when the page reports a relevant
/// change: assignment raises PropertyChanged only for values that actually changed. The byte totals stay
/// CANONICAL — UnitFormatConverter formats them at render time.
/// </summary>
public sealed partial class DeviceCapabilitiesMemorySectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesMemorySectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
        RefreshDerivedState();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryModuleCountDisplay))]
    public partial int MemoryModuleCount { get; private set; }

    public string MemoryModuleCountDisplay => MemoryModuleCount.ToString();

    [ObservableProperty]
    public partial ulong? MemoryTotalCapacityBytes { get; private set; }

    [ObservableProperty]
    public partial ulong? TotalPhysicalMemoryBytes { get; private set; }

    [ObservableProperty]
    public partial ulong? AvailablePhysicalMemoryBytes { get; private set; }

    [ObservableProperty]
    public partial ulong? TotalPageFileMemoryBytes { get; private set; }

    [ObservableProperty]
    public partial ulong? AvailablePageFileMemoryBytes { get; private set; }

    [ObservableProperty]
    public partial double PhysicalMemoryUsagePercent { get; private set; }

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.Brush? PhysicalMemoryUsageBarBrush { get; private set; }

    [ObservableProperty]
    public partial string PhysicalMemoryUsageDisplay { get; private set; } = "Unknown";

    [ObservableProperty]
    public partial Visibility PhysicalMemoryUsageSuccessVisibility { get; private set; }

    [ObservableProperty]
    public partial Visibility PhysicalMemoryUsageWarningVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility PhysicalMemoryUsageErrorVisibility { get; private set; } = Visibility.Collapsed;

    public ReadOnlyObservableCollection<DeviceCapabilitiesMemoryModuleCardModel> MemoryModuleCards => _parent.MemoryModuleCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void RefreshDerivedState()
    {
        MemoryModuleCount = _parent.MemoryModuleCount;
        MemoryTotalCapacityBytes = _parent.MemoryTotalCapacityBytes;
        TotalPhysicalMemoryBytes = _parent.TotalPhysicalMemoryBytes;
        AvailablePhysicalMemoryBytes = _parent.AvailablePhysicalMemoryBytes;
        TotalPageFileMemoryBytes = _parent.TotalPageFileMemoryBytes;
        AvailablePageFileMemoryBytes = _parent.AvailablePageFileMemoryBytes;
        PhysicalMemoryUsagePercent = _parent.PhysicalMemoryUsagePercent;
        PhysicalMemoryUsageBarBrush = _parent.PhysicalMemoryUsageBarBrush;
        PhysicalMemoryUsageDisplay = _parent.PhysicalMemoryUsageDisplay;
        PhysicalMemoryUsageSuccessVisibility = _parent.PhysicalMemoryUsageSuccessVisibility;
        PhysicalMemoryUsageWarningVisibility = _parent.PhysicalMemoryUsageWarningVisibility;
        PhysicalMemoryUsageErrorVisibility = _parent.PhysicalMemoryUsageErrorVisibility;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the page's "everything changed" signal, raised when the display units change —
        // the usage line is unit-formatted text.
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            RefreshDerivedState();

            // See the CPU section: a unit change moves no canonical value, so the broadcast has to pass
            // through or converter-bound tiles keep rendering in the old unit.
            OnPropertyChanged(propertyName: null);
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(DeviceCapabilitiesModel.Snapshot):
            case nameof(DeviceCapabilitiesModel.MemoryTotalCapacityBytes):
            case nameof(DeviceCapabilitiesModel.TotalPhysicalMemoryBytes):
            case nameof(DeviceCapabilitiesModel.AvailablePhysicalMemoryBytes):
            case nameof(DeviceCapabilitiesModel.TotalPageFileMemoryBytes):
            case nameof(DeviceCapabilitiesModel.AvailablePageFileMemoryBytes):
            case nameof(DeviceCapabilitiesModel.PhysicalMemoryUsageDisplay):
                RefreshDerivedState();
                break;
        }
    }
}
