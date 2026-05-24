using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// One editable point on a custom fan curve. Temperature is in whole-degree Celsius,
/// duty is a percent in [0, 100]. Persisted into the service via SetFanCustomCurve.
/// </summary>
public partial class CurvePointModel : ObservableObject
{
    public CurvePointModel(int temperatureCelsius, double dutyPercent)
    {
        TemperatureCelsius = temperatureCelsius;
        DutyPercent = dutyPercent;
    }

    [ObservableProperty]
    public partial int TemperatureCelsius { get; set; }

    [ObservableProperty]
    public partial double DutyPercent { get; set; }
}
