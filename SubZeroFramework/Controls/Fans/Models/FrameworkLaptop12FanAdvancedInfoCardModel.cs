using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop12FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ThermalCapacity { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeatPipeConfiguration { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double FanDiameterMillimeters { get; set; }

    [ObservableProperty]
    public partial double FanThicknessMillimeters { get; set; }

    [ObservableProperty]
    public partial string ThermalInterfaceMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int FirmwareMinimumRpm { get; set; }

    [ObservableProperty]
    public partial int FirmwareMaximumRpm { get; set; }

    [ObservableProperty]
    public partial int MaximumPhysicalLimitRpm { get; set; }

    public void UpdateFrom(FrameworkLaptop12CoolingDetails details)
    {
        ProcessorSupport = details.ProcessorSupport;
        ThermalCapacity = details.ThermalCapacity;
        HeatPipeConfiguration = details.HeatPipeConfiguration;
        FanDiameterMillimeters = details.FanDimensions.WidthMillimeters;
        FanThicknessMillimeters = details.FanDimensions.ThicknessMillimeters;
        ThermalInterfaceMaterial = details.ThermalInterfaceMaterial;
        FirmwareMinimumRpm = details.FirmwareOperatingRangeRpm.MinimumRpm;
        FirmwareMaximumRpm = details.FirmwareOperatingRangeRpm.MaximumRpm;
        MaximumPhysicalLimitRpm = details.MaximumPhysicalLimitRpm;
    }
}
