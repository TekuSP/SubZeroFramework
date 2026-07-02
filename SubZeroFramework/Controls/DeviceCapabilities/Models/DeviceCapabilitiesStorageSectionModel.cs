using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed partial class DeviceCapabilitiesStorageSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesStorageSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageDriveCount))]
    [NotifyPropertyChangedFor(nameof(StorageDriveCountDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalStorageCapacity))]
    [NotifyPropertyChangedFor(nameof(TotalStorageUsedSpace))]
    [NotifyPropertyChangedFor(nameof(TotalStorageFreeSpace))]
    [NotifyPropertyChangedFor(nameof(TotalStorageFreeBrush))]
    [NotifyPropertyChangedFor(nameof(TotalStorageUsagePercent))]
    [NotifyPropertyChangedFor(nameof(TotalStorageUsageBarBrush))]
    [NotifyPropertyChangedFor(nameof(TotalStorageUsageSummary))]
    private partial int SnapshotVersion { get; set; }

    public int StorageDriveCount => _parent.StorageDriveCount;

    public string StorageDriveCountDisplay => _parent.StorageDriveCount.ToString();

    public string TotalStorageCapacity => _parent.TotalStorageCapacity;

    public string TotalStorageUsedSpace => _parent.TotalStorageUsedSpace;

    public string TotalStorageFreeSpace => _parent.TotalStorageFreeSpace;

    public Microsoft.UI.Xaml.Media.Brush TotalStorageFreeBrush => _parent.TotalStorageFreeBrush;

    public double TotalStorageUsagePercent => _parent.TotalStorageUsagePercent;

    public Microsoft.UI.Xaml.Media.Brush TotalStorageUsageBarBrush => _parent.TotalStorageUsageBarBrush;

    public string TotalStorageUsageSummary => _parent.TotalStorageUsageSummary;

    public ReadOnlyObservableCollection<DeviceCapabilitiesStorageDriveCardModel> StorageDriveCards => _parent.StorageDriveCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DeviceCapabilitiesModel.Snapshot):
            case nameof(DeviceCapabilitiesModel.TotalStorageCapacity):
            case nameof(DeviceCapabilitiesModel.TotalStorageUsedSpace):
            case nameof(DeviceCapabilitiesModel.TotalStorageFreeSpace):
            case nameof(DeviceCapabilitiesModel.TotalStorageUsageSummary):
                SnapshotVersion++;
                break;
        }
    }
}