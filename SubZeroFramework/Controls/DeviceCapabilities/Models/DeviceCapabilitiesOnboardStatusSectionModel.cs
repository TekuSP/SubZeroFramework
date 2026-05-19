using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed class DeviceCapabilitiesOnboardStatusSectionModel : ObservableObject
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesOnboardStatusSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
    }

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> TemperatureStatusItems => _parent.TemperatureStatusItems;

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> FanStatusItems => _parent.FanStatusItems;

    public ReadOnlyObservableCollection<DeviceCapabilitiesRuntimeStatusItemModel> BatteryStatusItems => _parent.BatteryStatusItems;
}