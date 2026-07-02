using SubZeroFramework.Presentation.MenuItems.DeviceCapabilities;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>
/// Shared base for the per-category body ViewModels resolved by the Device Capabilities navigation sub-region.
/// Each is a thin slice over the shared page model: the section models it exposes are stable, get-only
/// properties created in the page model's constructor, so a one-time capture is sufficient (no re-subscription;
/// same accessor-bridge rationale as <c>FanModeModelBase</c>).
/// </summary>
public abstract class DeviceCapabilitiesCategoryModelBase
{
    protected DeviceCapabilitiesCategoryModelBase(DeviceCapabilitiesAccessor accessor)
    {
        // Read the page-driven model the displayed page published, NOT a DI-resolved one (Uno's nested
        // navigation would otherwise inject a separate, dead DeviceCapabilitiesModel).
        Page = accessor.Current
            ?? throw new InvalidOperationException(
                "Device Capabilities page model was not published before a category body was created.");
    }

    /// <summary>The shared page model driving every section.</summary>
    public DeviceCapabilitiesModel Page { get; }
}
