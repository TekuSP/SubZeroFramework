using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkDesktopFanOptionModel : ObservableObject
{
    [ObservableProperty]
    public partial string ModelName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double WidthMillimeters { get; set; }

    [ObservableProperty]
    public partial double HeightMillimeters { get; set; }

    [ObservableProperty]
    public partial double ThicknessMillimeters { get; set; }

    [ObservableProperty]
    public partial string ConnectorType { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumAirflowDisplay))]
    public partial double MaximumAirflowCfm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumAirflowDisplay))]
    public partial string? AlternateAirflowDisplay { get; set; }

    [ObservableProperty]
    public partial string AcousticNoiseDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int MaximumFanSpeedRpm { get; set; }

    public string MaximumAirflowDisplay => string.IsNullOrWhiteSpace(AlternateAirflowDisplay)
        ? $"{MaximumAirflowCfm} CFM"
        : $"{MaximumAirflowCfm} CFM ({AlternateAirflowDisplay})";

    public void UpdateFrom(FrameworkDesktopFanOption option)
    {
        ModelName = option.ModelName;
        WidthMillimeters = option.FanDimensions.WidthMillimeters;
        HeightMillimeters = option.FanDimensions.HeightMillimeters;
        ThicknessMillimeters = option.FanDimensions.ThicknessMillimeters;
        ConnectorType = option.ConnectorType;
        MaximumAirflowCfm = option.MaximumAirflowCfm;
        AlternateAirflowDisplay = option.AlternateAirflowDisplay;
        AcousticNoiseDisplay = option.AcousticNoiseDisplay;
        MaximumFanSpeedRpm = option.MaximumFanSpeedRpm;
    }
}
