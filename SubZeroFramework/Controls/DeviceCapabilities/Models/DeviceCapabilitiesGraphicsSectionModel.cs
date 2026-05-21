using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed partial class DeviceCapabilitiesGraphicsSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesGraphicsSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GraphicsAdapterCount))]
    [NotifyPropertyChangedFor(nameof(MonitorCount))]
    [NotifyPropertyChangedFor(nameof(ActiveMonitorCount))]
    private partial int SnapshotVersion { get; set; }

    public int GraphicsAdapterCount => _parent.GraphicsAdapterCount;

    public int MonitorCount => _parent.MonitorCount;

    public int ActiveMonitorCount => _parent.ActiveMonitorCount;

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorResolutionCard> MonitorResolutionCards => _parent.MonitorResolutionCards;

    public ReadOnlyObservableCollection<DeviceCapabilitiesGraphicsCardGroupModel> GraphicsCardGroups => _parent.GraphicsCardGroups;

    public ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel> VideoControllerCards => _parent.VideoControllerCards;

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel> MonitorCards => _parent.MonitorCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceCapabilitiesModel.Snapshot))
        {
            SnapshotVersion++;
        }
    }
}