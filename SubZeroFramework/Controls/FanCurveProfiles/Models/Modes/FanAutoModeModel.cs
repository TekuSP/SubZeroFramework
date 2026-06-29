using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

/// <summary>
/// Body ViewModel for the Auto mode route: the embedded-controller policy. Adds nothing beyond the shared
/// gauge + description (<see cref="FanModeModelBase"/>).
/// </summary>
public sealed partial class FanAutoModeModel : FanModeModelBase
{
    public FanAutoModeModel(FanCoordinatorAccessor coordinatorAccessor) : base(coordinatorAccessor)
    {
    }
}
