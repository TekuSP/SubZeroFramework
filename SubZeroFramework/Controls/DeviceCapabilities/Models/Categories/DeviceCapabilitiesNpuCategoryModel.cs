using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Body ViewModel for the Neural processor category route (NPU devices and their live utilization).</summary>
public sealed class DeviceCapabilitiesNpuCategoryModel(DeviceCapabilitiesAccessor accessor) : DeviceCapabilitiesCategoryModelBase(accessor);
