using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Body ViewModel for the Onboard devices category route (temperature sensors, fans, batteries).</summary>
public sealed class DeviceCapabilitiesOnboardCategoryModel(DeviceCapabilitiesAccessor accessor) : DeviceCapabilitiesCategoryModelBase(accessor)
{
    public DeviceCapabilitiesOnboardStatusSectionModel Section => Page.OnboardStatusSection;
}
