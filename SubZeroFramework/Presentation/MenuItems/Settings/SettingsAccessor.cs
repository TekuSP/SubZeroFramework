namespace SubZeroFramework.Presentation.MenuItems.Settings;

/// <summary>
/// Bridges the live Settings page model to the navigation-resolved section body ViewModels.
///
/// Uno's nested-region navigation constructs each section VM (<c>SettingsServiceSectionModel</c> etc.) with
/// DI-resolved arguments — not the page instance the user actually sees — so the section bodies would
/// otherwise observe a separate, dead <see cref="SettingsModel"/>. The displayed page model publishes itself
/// here in its constructor (it is always built before the section region navigates); the section VMs read
/// <see cref="Current"/> instead of taking the page model via DI, guaranteeing they share the one instance
/// the user interacts with. Same pattern as <c>DeviceCapabilitiesAccessor</c>.
/// </summary>
public sealed class SettingsAccessor
{
    /// <summary>The page-driven model instance, set by <see cref="SettingsModel"/>'s constructor.</summary>
    public SettingsModel? Current { get; set; }
}
