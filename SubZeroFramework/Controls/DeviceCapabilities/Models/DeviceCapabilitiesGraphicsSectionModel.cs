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
    [NotifyPropertyChangedFor(nameof(GraphicsAdapterCountDisplay))]
    [NotifyPropertyChangedFor(nameof(MonitorCountDisplay))]
    [NotifyPropertyChangedFor(nameof(ActiveMonitorCountDisplay))]
    [NotifyPropertyChangedFor(nameof(PrimaryDisplayName))]
    [NotifyPropertyChangedFor(nameof(PrimaryDisplayBadge))]
    [NotifyPropertyChangedFor(nameof(MonitorCount))]
    [NotifyPropertyChangedFor(nameof(ActiveMonitorCount))]
    private partial int SnapshotVersion { get; set; }

    public int GraphicsAdapterCount => _parent.GraphicsAdapterCount;

    public string GraphicsAdapterCountDisplay => _parent.GraphicsAdapterCount.ToString();

    public string MonitorCountDisplay => _parent.MonitorCount.ToString();

    public string ActiveMonitorCountDisplay => _parent.ActiveMonitorCount.ToString();

    public string PrimaryDisplayName => _parent.PrimaryDisplayName;

    public string PrimaryDisplayBadge => _parent.PrimaryDisplayBadge;

    public int MonitorCount => _parent.MonitorCount;

    public int ActiveMonitorCount => _parent.ActiveMonitorCount;

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