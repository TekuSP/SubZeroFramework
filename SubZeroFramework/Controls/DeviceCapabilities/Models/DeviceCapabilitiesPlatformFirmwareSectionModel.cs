using System;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Models;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed partial class DeviceCapabilitiesPlatformFirmwareSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesPlatformFirmwareSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Snapshot))]
    private partial int SnapshotVersion { get; set; }

    public HardwareInfoSnapshot? Snapshot => _parent.Snapshot;

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
