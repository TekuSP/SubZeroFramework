using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

public sealed class DeviceCapabilitiesNetworkSectionModel : ObservableObject
{
    private readonly DeviceCapabilitiesModel _parent;

    public DeviceCapabilitiesNetworkSectionModel(DeviceCapabilitiesModel parent)
    {
        _parent = parent;
    }

    public ReadOnlyObservableCollection<DeviceCapabilitiesNetworkAdapterCardModel> NetworkAdapterCards => _parent.NetworkAdapterCards;
}