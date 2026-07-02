namespace SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

/// <summary>
/// Bridges the live Device Capabilities page model to the navigation-resolved category body ViewModels.
///
/// Uno's nested-region navigation constructs each category VM (<c>DeviceCapabilitiesCpuCategoryModel</c> etc.)
/// with DI-resolved arguments — not the page instance the user actually sees — so the category bodies would
/// otherwise observe a separate, dead <see cref="DeviceCapabilitiesModel"/>. The displayed page model publishes
/// itself here in its constructor (it is always built before the category region navigates); the category VMs
/// read <see cref="Current"/> instead of taking the page model via DI, guaranteeing they share the one instance
/// the user interacts with. Same pattern as <c>FanCoordinatorAccessor</c>.
/// </summary>
public sealed class DeviceCapabilitiesAccessor
{
    /// <summary>The page-driven model instance, set by <see cref="DeviceCapabilitiesModel"/>'s constructor.</summary>
    public DeviceCapabilitiesModel? Current { get; set; }
}
