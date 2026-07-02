namespace SubZeroFramework.Presentation.MenuItems.Modules;

/// <summary>
/// Bridges the live Modules page model to the navigation-resolved layout body ViewModels.
///
/// Uno's nested-region navigation constructs each layout VM (<c>ModulesFw16Model</c> etc.) with DI-resolved
/// arguments — not the page instance the user actually sees — so the layout bodies would otherwise observe a
/// separate, dead <see cref="ModulesModel"/>. The displayed page model publishes itself here in its constructor
/// (it is always built before the layout region navigates); the layout VMs read <see cref="Current"/> instead of
/// taking the page model via DI, guaranteeing they share the one instance the user interacts with. Same pattern
/// as <c>DeviceCapabilitiesAccessor</c> / <c>FanCoordinatorAccessor</c>.
/// </summary>
public sealed class ModulesAccessor
{
    /// <summary>The page-driven model instance, set by <see cref="ModulesModel"/>'s constructor.</summary>
    public ModulesModel? Current { get; set; }
}
