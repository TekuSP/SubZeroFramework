using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Presentation.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop16FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkLaptop16FanAdvancedInfoCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
    }

    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PrimaryCpuThermalInterfaceMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellFanDimensionsDisplay))]
    public partial double ShellFanWidthMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellFanDimensionsDisplay))]
    public partial double ShellFanHeightMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellFanDimensionsDisplay))]
    public partial double ShellFanThicknessMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GraphicsFanDimensionsDisplay))]
    public partial double GraphicsFanWidthMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GraphicsFanDimensionsDisplay))]
    public partial double GraphicsFanHeightMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GraphicsFanDimensionsDisplay))]
    public partial double GraphicsFanThicknessMillimeters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpansionBayPowerLimitDisplay))]
    public partial double ExpansionBayPowerLimitWatts { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StandardFirmwareMaximumDisplay))]
    public partial int StandardFirmwareMaximumRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproximateThermalStressMaximumDisplay))]
    public partial int ApproximateThermalStressMaximumRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShellFanDimensionsDisplay))]
    [NotifyPropertyChangedFor(nameof(GraphicsFanDimensionsDisplay))]
    [NotifyPropertyChangedFor(nameof(ExpansionBayPowerLimitDisplay))]
    [NotifyPropertyChangedFor(nameof(StandardFirmwareMaximumDisplay))]
    [NotifyPropertyChangedFor(nameof(ApproximateThermalStressMaximumDisplay))]
    private partial int UnitFormattingRevision { get; set; }

    public string ShellFanDimensionsDisplay => BuildDimensionsDisplay(ShellFanWidthMillimeters, ShellFanHeightMillimeters, ShellFanThicknessMillimeters);

    public string GraphicsFanDimensionsDisplay => BuildDimensionsDisplay(GraphicsFanWidthMillimeters, GraphicsFanHeightMillimeters, GraphicsFanThicknessMillimeters);

    public string ExpansionBayPowerLimitDisplay => $"{_unitFormattingService.FormatPowerWatts(ExpansionBayPowerLimitWatts)} allocated maximum";

    public string StandardFirmwareMaximumDisplay => _unitFormattingService.FormatFanSpeed(StandardFirmwareMaximumRpm);

    public string ApproximateThermalStressMaximumDisplay => $"~{_unitFormattingService.FormatFanSpeed(ApproximateThermalStressMaximumRpm)}";

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

    public override void RefreshUnitFormatting()
    {
        UnitFormattingRevision++;
    }

    private string BuildDimensionsDisplay(double widthMillimeters, double heightMillimeters, double thicknessMillimeters)
    {
        return $"{_unitFormattingService.FormatLengthMillimeters(widthMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(heightMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(thicknessMillimeters)}";
    }
}
