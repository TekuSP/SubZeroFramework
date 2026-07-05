using SubZeroFramework.Presentation.MenuItems.Settings;

namespace SubZeroFramework.Controls.Settings.Models.Sections;

/// <summary>
/// Shared base for the per-section body ViewModels resolved by the Settings navigation sub-region. Each is
/// a thin slice over the shared page model: the state it exposes is created in the page model's constructor,
/// so a one-time capture is sufficient (same accessor-bridge rationale as
/// <c>DeviceCapabilitiesCategoryModelBase</c>).
/// </summary>
public abstract class SettingsSectionModelBase
{
    protected SettingsSectionModelBase(SettingsAccessor accessor)
    {
        // Read the page-driven model the displayed page published, NOT a DI-resolved one (Uno's nested
        // navigation would otherwise inject a separate, dead SettingsModel).
        Page = accessor.Current
            ?? throw new InvalidOperationException("Settings page model was not published before a section body was created.");
    }

    /// <summary>The shared page model driving every section.</summary>
    public SettingsModel Page { get; }
}

/// <summary>Background-service lifecycle section ("Service").</summary>
public sealed class SettingsServiceSectionModel(SettingsAccessor accessor) : SettingsSectionModelBase(accessor);

/// <summary>Display-units section powered by UnitsNet.</summary>
public sealed class SettingsUnitsSectionModel(SettingsAccessor accessor) : SettingsSectionModelBase(accessor);

/// <summary>Launch behavior & notification opt-ins section ("Startup & alerts").</summary>
public sealed class SettingsStartupSectionModel(SettingsAccessor accessor) : SettingsSectionModelBase(accessor);

/// <summary>Open-source notices section ("Licenses").</summary>
public sealed class SettingsLicensesSectionModel(SettingsAccessor accessor) : SettingsSectionModelBase(accessor);

/// <summary>Version information and project links section ("About").</summary>
public sealed class SettingsAboutSectionModel(SettingsAccessor accessor) : SettingsSectionModelBase(accessor);
