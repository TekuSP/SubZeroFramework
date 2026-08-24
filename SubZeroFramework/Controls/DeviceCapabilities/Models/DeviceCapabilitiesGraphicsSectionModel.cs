using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// The Graphics section's slice over the Device Capabilities page model. Every figure it shows is MIRRORED
/// as a stored property that <see cref="RefreshDerivedState"/> reassigns when the page's snapshot changes:
/// assignment raises PropertyChanged only for values that actually changed.
/// </summary>
public sealed partial class DeviceCapabilitiesGraphicsSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesGraphicsSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
        RefreshDerivedState();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GraphicsAdapterCountDisplay))]
    public partial int GraphicsAdapterCount { get; private set; }

    public string GraphicsAdapterCountDisplay => GraphicsAdapterCount.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorCountDisplay))]
    public partial int MonitorCount { get; private set; }

    public string MonitorCountDisplay => MonitorCount.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMonitorCountDisplay))]
    public partial int ActiveMonitorCount { get; private set; }

    public string ActiveMonitorCountDisplay => ActiveMonitorCount.ToString();

    [ObservableProperty]
    public partial string PrimaryDisplayName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PrimaryDisplayBadge { get; private set; } = string.Empty;

    public ReadOnlyObservableCollection<DeviceCapabilitiesGraphicsCardGroupModel> GraphicsCardGroups => _parent.GraphicsCardGroups;

    public ReadOnlyObservableCollection<DeviceCapabilitiesVideoControllerCardModel> VideoControllerCards => _parent.VideoControllerCards;

    public ReadOnlyObservableCollection<DeviceCapabilitiesMonitorCardModel> MonitorCards => _parent.MonitorCards;

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void RefreshDerivedState()
    {
        GraphicsAdapterCount = _parent.GraphicsAdapterCount;
        MonitorCount = _parent.MonitorCount;
        ActiveMonitorCount = _parent.ActiveMonitorCount;
        PrimaryDisplayName = _parent.PrimaryDisplayName;
        PrimaryDisplayBadge = _parent.PrimaryDisplayBadge;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The empty name is the page's "everything changed" signal (display-unit change); the counts and
        // names do not move with units, but re-mirroring them is a set of no-op assignments.
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(DeviceCapabilitiesModel.Snapshot))
        {
            RefreshDerivedState();
        }
    }
}
