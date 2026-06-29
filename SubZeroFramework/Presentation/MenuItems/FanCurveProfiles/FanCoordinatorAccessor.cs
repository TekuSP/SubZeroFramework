namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// Bridges the live Fan Control page coordinator to the navigation-resolved mode body ViewModels.
///
/// Uno's nested-region navigation constructs each mode VM (<c>FanAutoModeModel</c> etc.) from a <em>separate</em>
/// DI-resolved <see cref="FanCurveProfilesModel"/> — not the instance the page actually displays and drives — so
/// the mode bodies would observe a coordinator whose <c>SelectedFan</c> is never set (blank gauge / targets).
/// The displayed coordinator publishes itself here in its constructor (it is always built before the mode region
/// navigates); the mode VMs read <see cref="Current"/> instead of taking a coordinator via DI, guaranteeing they
/// share the one instance the user interacts with.
/// </summary>
public sealed class FanCoordinatorAccessor
{
    /// <summary>The page-driven coordinator instance, set by <see cref="FanCurveProfilesModel"/>'s constructor.</summary>
    public FanCurveProfilesModel? Current { get; set; }
}
