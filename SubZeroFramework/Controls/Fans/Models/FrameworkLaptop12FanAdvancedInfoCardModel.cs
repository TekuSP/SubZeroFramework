using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop12FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkLaptop12FanAdvancedInfoCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        RefreshUnitFormatting();
    }

    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ThermalCapacity { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeatPipeConfiguration { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double FanDiameterMillimeters { get; set; }

    partial void OnFanDiameterMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double FanThicknessMillimeters { get; set; }

    partial void OnFanThicknessMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string ThermalInterfaceMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int FirmwareMinimumRpm { get; set; }

    partial void OnFirmwareMinimumRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int FirmwareMaximumRpm { get; set; }

    partial void OnFirmwareMaximumRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int MaximumPhysicalLimitRpm { get; set; }

    partial void OnMaximumPhysicalLimitRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string FanDimensionsDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FirmwareRangeDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MaximumPhysicalLimitDisplay { get; private set; } = string.Empty;

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

    public override void RefreshUnitFormatting()
    {
        FanDimensionsDisplay = $"{_unitFormattingService.FormatLengthMillimeters(FanDiameterMillimeters)} diameter × {_unitFormattingService.FormatLengthMillimeters(FanThicknessMillimeters)} thickness";
        FirmwareRangeDisplay = $"{_unitFormattingService.FormatFanSpeedValue(FirmwareMinimumRpm)}–{_unitFormattingService.FormatFanSpeedValue(FirmwareMaximumRpm)} {_unitFormattingService.FanSpeedUnitSuffix}";
        MaximumPhysicalLimitDisplay = _unitFormattingService.FormatFanSpeed(MaximumPhysicalLimitRpm);
    }
}
