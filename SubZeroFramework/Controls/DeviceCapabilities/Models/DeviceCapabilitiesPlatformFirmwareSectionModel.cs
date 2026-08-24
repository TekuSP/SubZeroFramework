using System;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Models;
using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>
/// The Platform &amp; Firmware section's slice over the Device Capabilities page model: it only needs the
/// snapshot itself, MIRRORED as a stored property so its own assignment raises PropertyChanged.
/// </summary>
public sealed partial class DeviceCapabilitiesPlatformFirmwareSectionModel : ObservableObject, IDisposable
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesPlatformFirmwareSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
        _parent.PropertyChanged += ParentPropertyChanged;
        Snapshot = parent.Snapshot;
    }

    [ObservableProperty]
    public partial HardwareInfoSnapshot? Snapshot { get; private set; }

    public void Dispose()
    {
        _parent.PropertyChanged -= ParentPropertyChanged;
    }

    private void ParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(DeviceCapabilitiesModel.Snapshot))
        {
            Snapshot = _parent.Snapshot;
        }
    }
}
