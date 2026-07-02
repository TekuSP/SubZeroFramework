namespace SubZeroFramework.Presentation.MenuItems.Modules.Layouts;

/// <summary>Body ViewModel for the Framework 12 modules layout route (accessor bridge, see ModulesAccessor).</summary>
public sealed class ModulesFw12Model(ModulesAccessor accessor)
{
    public ModulesModel Page { get; } = accessor.Current
        ?? throw new InvalidOperationException("The Modules page model must exist before its layout body navigates.");
}
