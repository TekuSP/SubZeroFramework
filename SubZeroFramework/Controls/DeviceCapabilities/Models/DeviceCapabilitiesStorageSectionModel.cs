using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// The Storage section's slice over the Device Capabilities page model. Every figure it shows is MIRRORED as
/// a stored property that <see cref="RefreshDerivedState"/> reassigns when the page reports a relevant
/// change: assignment raises PropertyChanged only for values that actually changed. The byte totals stay
/// CANONICAL — UnitFormatConverter formats them at render time.
/// </summary>
public sealed partial class DeviceCapabilitiesStorageSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesStorageSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
        RefreshDerivedState();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageDriveCountDisplay))]
    public partial int StorageDriveCount { get; private set; }

    public string StorageDriveCountDisplay => StorageDriveCount.ToString();

    [ObservableProperty]
    public partial ulong? TotalStorageCapacityBytes { get; private set; }

    [ObservableProperty]
    public partial ulong? TotalStorageUsedSpaceBytes { get; private set; }

    [ObservableProperty]
    public partial ulong? TotalStorageFreeSpaceBytes { get; private set; }

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.Brush? TotalStorageFreeBrush { get; private set; }

    [ObservableProperty]
    public partial double TotalStorageUsagePercent { get; private set; }

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.Brush? TotalStorageUsageBarBrush { get; private set; }

    [ObservableProperty]
    public partial string TotalStorageUsageSummary { get; private set; } = "Unknown";

    public ReadOnlyObservableCollection<DeviceCapabilitiesStorageDriveCardModel> StorageDriveCards => _parent.StorageDriveCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void RefreshDerivedState()
    {
        StorageDriveCount = _parent.StorageDriveCount;
        TotalStorageCapacityBytes = _parent.TotalStorageCapacityBytes;
        TotalStorageUsedSpaceBytes = _parent.TotalStorageUsedSpaceBytes;
        TotalStorageFreeSpaceBytes = _parent.TotalStorageFreeSpaceBytes;
        TotalStorageFreeBrush = _parent.TotalStorageFreeBrush;
        TotalStorageUsagePercent = _parent.TotalStorageUsagePercent;
        TotalStorageUsageBarBrush = _parent.TotalStorageUsageBarBrush;
        TotalStorageUsageSummary = _parent.TotalStorageUsageSummary;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name is the page's "everything changed" signal, raised when the display units change —
        // the usage summary is unit-formatted text.
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
            case nameof(DeviceCapabilitiesModel.TotalStorageCapacityBytes):
            case nameof(DeviceCapabilitiesModel.TotalStorageUsedSpaceBytes):
            case nameof(DeviceCapabilitiesModel.TotalStorageFreeSpaceBytes):
            case nameof(DeviceCapabilitiesModel.TotalStorageUsageSummary):
                RefreshDerivedState();
                break;
        }
    }
}
