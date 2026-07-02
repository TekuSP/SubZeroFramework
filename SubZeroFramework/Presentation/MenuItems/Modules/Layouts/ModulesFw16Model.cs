namespace SubZeroFramework.Presentation.MenuItems.Modules.Layouts;

/// <summary>Body ViewModel for the Framework 16 modules layout route; binds to the live page model via
/// <see cref="ModulesAccessor"/> (nested-region VMs are DI-constructed, not the displayed page instance).</summary>
public sealed class ModulesFw16Model(ModulesAccessor accessor)
{
    public ModulesModel Page { get; } = accessor.Current
        ?? throw new InvalidOperationException("The Modules page model must exist before its layout body navigates.");
}
