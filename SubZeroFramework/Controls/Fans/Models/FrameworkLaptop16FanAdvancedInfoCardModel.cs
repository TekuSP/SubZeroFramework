using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop16FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PrimaryCpuThermalInterfaceMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ShellFanWidthMillimeters { get; set; }

    [ObservableProperty]
    public partial double ShellFanHeightMillimeters { get; set; }

    [ObservableProperty]
    public partial double ShellFanThicknessMillimeters { get; set; }

    [ObservableProperty]
    public partial double GraphicsFanWidthMillimeters { get; set; }

    [ObservableProperty]
    public partial double GraphicsFanHeightMillimeters { get; set; }

    [ObservableProperty]
    public partial double GraphicsFanThicknessMillimeters { get; set; }

    [ObservableProperty]
    public partial double ExpansionBayPowerLimitWatts { get; set; }

    [ObservableProperty]
    public partial int StandardFirmwareMaximumRpm { get; set; }

    [ObservableProperty]
    public partial int ApproximateThermalStressMaximumRpm { get; set; }

    public void UpdateFrom(FrameworkLaptop16CoolingDetails details)
    {
        ProcessorSupport = details.ProcessorSupport;
        PrimaryCpuThermalInterfaceMaterial = details.PrimaryCpuThermalInterfaceMaterial;
        ShellFanWidthMillimeters = details.ShellFanDimensions.WidthMillimeters;
        ShellFanHeightMillimeters = details.ShellFanDimensions.HeightMillimeters;
        ShellFanThicknessMillimeters = details.ShellFanDimensions.ThicknessMillimeters;
        GraphicsFanWidthMillimeters = details.GraphicsFanDimensions.WidthMillimeters;
        GraphicsFanHeightMillimeters = details.GraphicsFanDimensions.HeightMillimeters;
        GraphicsFanThicknessMillimeters = details.GraphicsFanDimensions.ThicknessMillimeters;
        ExpansionBayPowerLimitWatts = details.ExpansionBayPowerLimitWatts;
        StandardFirmwareMaximumRpm = details.StandardFirmwareMaximumRpm;
        ApproximateThermalStressMaximumRpm = details.ApproximateThermalStressMaximumRpm;
    }
}
