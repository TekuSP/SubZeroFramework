using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Body ViewModel for the Graphics category route (adapters, monitors).</summary>
public sealed class DeviceCapabilitiesGraphicsCategoryModel(DeviceCapabilitiesAccessor accessor) : DeviceCapabilitiesCategoryModelBase(accessor)
{
    public DeviceCapabilitiesGraphicsSectionModel Section => Page.GraphicsSection;
}
