using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// Toggleable temperature sensor chip used by the Custom curve sensor selector.
/// </summary>
public partial class SensorChipModel : ObservableObject
{
    public SensorChipModel(int sensorIndex, string displayName)
    {
        SensorIndex = sensorIndex;
        DisplayName = displayName;
    }

    public int SensorIndex { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial double? CurrentTemperatureCelsius { get; set; }
}
