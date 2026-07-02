using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed partial class DeviceCapabilitiesMemorySectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesMemorySectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryModuleCount))]
    [NotifyPropertyChangedFor(nameof(MemoryModuleCountDisplay))]
    [NotifyPropertyChangedFor(nameof(MemoryTotalCapacity))]
    [NotifyPropertyChangedFor(nameof(TotalPhysicalMemory))]
    [NotifyPropertyChangedFor(nameof(AvailablePhysicalMemory))]
    [NotifyPropertyChangedFor(nameof(TotalPageFileMemory))]
    [NotifyPropertyChangedFor(nameof(AvailablePageFileMemory))]
    [NotifyPropertyChangedFor(nameof(PhysicalMemoryUsagePercent))]
    [NotifyPropertyChangedFor(nameof(PhysicalMemoryUsageBarBrush))]
    [NotifyPropertyChangedFor(nameof(PhysicalMemoryUsageDisplay))]
    [NotifyPropertyChangedFor(nameof(PhysicalMemoryUsageSuccessVisibility))]
    [NotifyPropertyChangedFor(nameof(PhysicalMemoryUsageWarningVisibility))]
    [NotifyPropertyChangedFor(nameof(PhysicalMemoryUsageErrorVisibility))]
    private partial int SnapshotVersion { get; set; }

    public int MemoryModuleCount => _parent.MemoryModuleCount;

    public string MemoryModuleCountDisplay => _parent.MemoryModuleCount.ToString();

    public string MemoryTotalCapacity => _parent.MemoryTotalCapacity;

    public string TotalPhysicalMemory => _parent.TotalPhysicalMemory;

    public string AvailablePhysicalMemory => _parent.AvailablePhysicalMemory;

    public string TotalPageFileMemory => _parent.TotalPageFileMemory;

    public string AvailablePageFileMemory => _parent.AvailablePageFileMemory;

    public double PhysicalMemoryUsagePercent => _parent.PhysicalMemoryUsagePercent;

    public Microsoft.UI.Xaml.Media.Brush PhysicalMemoryUsageBarBrush => _parent.PhysicalMemoryUsageBarBrush;

    public string PhysicalMemoryUsageDisplay => _parent.PhysicalMemoryUsageDisplay;

    public Visibility PhysicalMemoryUsageSuccessVisibility => _parent.PhysicalMemoryUsageSuccessVisibility;

    public Visibility PhysicalMemoryUsageWarningVisibility => _parent.PhysicalMemoryUsageWarningVisibility;

    public Visibility PhysicalMemoryUsageErrorVisibility => _parent.PhysicalMemoryUsageErrorVisibility;

    public ReadOnlyObservableCollection<DeviceCapabilitiesMemoryModuleCardModel> MemoryModuleCards => _parent.MemoryModuleCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DeviceCapabilitiesModel.Snapshot):
            case nameof(DeviceCapabilitiesModel.MemoryTotalCapacity):
            case nameof(DeviceCapabilitiesModel.TotalPhysicalMemory):
            case nameof(DeviceCapabilitiesModel.AvailablePhysicalMemory):
            case nameof(DeviceCapabilitiesModel.TotalPageFileMemory):
            case nameof(DeviceCapabilitiesModel.AvailablePageFileMemory):
            case nameof(DeviceCapabilitiesModel.PhysicalMemoryUsageDisplay):
                SnapshotVersion++;
                break;
        }
    }
}