using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Body ViewModel for the Storage category route (usage bar, detected drives).</summary>
public sealed class DeviceCapabilitiesStorageCategoryModel(DeviceCapabilitiesAccessor accessor) : DeviceCapabilitiesCategoryModelBase(accessor)
{
    public DeviceCapabilitiesStorageSectionModel Section => Page.StorageSection;
}
