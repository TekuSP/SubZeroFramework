using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

/// <summary>
/// Body ViewModel for the Max mode route: forced 100% duty. Adds nothing beyond the shared gauge +
/// description (<see cref="FanModeModelBase"/>); the ghost arc target (100%) comes from the coordinator.
/// </summary>
public sealed partial class FanMaxModeModel : FanModeModelBase
{
    public FanMaxModeModel(FanCoordinatorAccessor coordinatorAccessor) : base(coordinatorAccessor)
    {
    }
}
