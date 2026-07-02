using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Body ViewModel for the Network category route (detected adapters, connectivity).</summary>
public sealed class DeviceCapabilitiesNetworkCategoryModel(DeviceCapabilitiesAccessor accessor) : DeviceCapabilitiesCategoryModelBase(accessor)
{
    public DeviceCapabilitiesNetworkSectionModel Section => Page.NetworkSection;
}
