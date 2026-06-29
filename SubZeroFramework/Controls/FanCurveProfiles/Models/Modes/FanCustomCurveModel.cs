using SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models.Modes;

/// <summary>
/// Body ViewModel for the Custom curve mode route. The custom editing surface (curve points, sensors,
/// driving chart, staging) is large and reused wholesale, so this exposes the shared coordinator via
/// <see cref="FanModeModelBase.Page"/> and the custom view hosts the existing curve editor bound to it.
/// (Later step: split the curve + sensor logic into dedicated ViewModels.)
/// </summary>
public sealed partial class FanCustomCurveModel : FanModeModelBase
{
    public FanCustomCurveModel(FanCoordinatorAccessor coordinatorAccessor) : base(coordinatorAccessor)
    {
    }
}
