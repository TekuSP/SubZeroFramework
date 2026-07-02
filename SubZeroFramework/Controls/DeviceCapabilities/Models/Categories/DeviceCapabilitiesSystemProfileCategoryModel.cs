using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>
/// Body ViewModel for the System profile category route: the profile column plus the cooling-hardware and
/// platform-firmware blocks folded in (per the redesign mockup).
/// </summary>
public sealed class DeviceCapabilitiesSystemProfileCategoryModel(DeviceCapabilitiesAccessor accessor) : DeviceCapabilitiesCategoryModelBase(accessor)
{
    public DeviceCapabilitiesSystemProfileSectionModel ProfileSection => Page.SystemProfileSection;

    public DeviceCapabilitiesCoolingSectionModel CoolingSection => Page.CoolingSection;

    public DeviceCapabilitiesPlatformFirmwareSectionModel FirmwareSection => Page.PlatformFirmwareSection;
}
