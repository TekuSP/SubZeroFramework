using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Body ViewModel for the Memory category route (RAM usage, installed modules).</summary>
public sealed class DeviceCapabilitiesMemoryCategoryModel(DeviceCapabilitiesAccessor accessor) : DeviceCapabilitiesCategoryModelBase(accessor)
{
    public DeviceCapabilitiesMemorySectionModel Section => Page.MemorySection;
}
