using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop12FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkLaptop12FanAdvancedInfoCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
    }

    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ThermalCapacity { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeatPipeConfiguration { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanDimensionsDisplay))]
    public partial double FanDiameterMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanDimensionsDisplay))]
    public partial double FanThicknessMillimeters { get; set; }

    [ObservableProperty]
    public partial string ThermalInterfaceMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareRangeDisplay))]
    public partial int FirmwareMinimumRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareRangeDisplay))]
    public partial int FirmwareMaximumRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumPhysicalLimitDisplay))]
    public partial int MaximumPhysicalLimitRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FanDimensionsDisplay))]
    [NotifyPropertyChangedFor(nameof(FirmwareRangeDisplay))]
    [NotifyPropertyChangedFor(nameof(MaximumPhysicalLimitDisplay))]
    private partial int UnitFormattingRevision { get; set; }

    public string FanDimensionsDisplay => $"{_unitFormattingService.FormatLengthMillimeters(FanDiameterMillimeters)} diameter × {_unitFormattingService.FormatLengthMillimeters(FanThicknessMillimeters)} thickness";

    public string FirmwareRangeDisplay => $"{_unitFormattingService.FormatFanSpeedValue(FirmwareMinimumRpm)}–{_unitFormattingService.FormatFanSpeedValue(FirmwareMaximumRpm)} {_unitFormattingService.FanSpeedUnitSuffix}";

    public string MaximumPhysicalLimitDisplay => _unitFormattingService.FormatFanSpeed(MaximumPhysicalLimitRpm);

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
        UnitFormattingRevision++;
    }
}
