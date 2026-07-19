using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop16FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkLaptop16FanAdvancedInfoCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        RefreshUnitFormatting();
    }

    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PrimaryCpuThermalInterfaceMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ShellFanWidthMillimeters { get; set; }

    partial void OnShellFanWidthMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double ShellFanHeightMillimeters { get; set; }

    partial void OnShellFanHeightMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double ShellFanThicknessMillimeters { get; set; }

    partial void OnShellFanThicknessMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double GraphicsFanWidthMillimeters { get; set; }

    partial void OnGraphicsFanWidthMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double GraphicsFanHeightMillimeters { get; set; }

    partial void OnGraphicsFanHeightMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double GraphicsFanThicknessMillimeters { get; set; }

    partial void OnGraphicsFanThicknessMillimetersChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial double ExpansionBayPowerLimitWatts { get; set; }

    partial void OnExpansionBayPowerLimitWattsChanged(double value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int StandardFirmwareMaximumRpm { get; set; }

    partial void OnStandardFirmwareMaximumRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int ApproximateThermalStressMaximumRpm { get; set; }

    partial void OnApproximateThermalStressMaximumRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string ShellFanDimensionsDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GraphicsFanDimensionsDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ShellFanThicknessDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GraphicsFanThicknessDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ExpansionBayPowerLimitDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StandardFirmwareMaximumDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ApproximateThermalStressMaximumDisplay { get; private set; } = string.Empty;

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
        ShellFanDimensionsDisplay = BuildDimensionsDisplay(ShellFanWidthMillimeters, ShellFanHeightMillimeters, ShellFanThicknessMillimeters);
        GraphicsFanDimensionsDisplay = BuildDimensionsDisplay(GraphicsFanWidthMillimeters, GraphicsFanHeightMillimeters, GraphicsFanThicknessMillimeters);
        ShellFanThicknessDisplay = $"{_unitFormattingService.FormatLengthMillimeters(ShellFanThicknessMillimeters)} thick";
        GraphicsFanThicknessDisplay = $"{_unitFormattingService.FormatLengthMillimeters(GraphicsFanThicknessMillimeters)} thick";
        ExpansionBayPowerLimitDisplay = $"{_unitFormattingService.FormatPowerWatts(ExpansionBayPowerLimitWatts)} max";
        StandardFirmwareMaximumDisplay = _unitFormattingService.FormatFanSpeed(StandardFirmwareMaximumRpm);
        ApproximateThermalStressMaximumDisplay = $"~{_unitFormattingService.FormatFanSpeed(ApproximateThermalStressMaximumRpm)}";
    }

    private string BuildDimensionsDisplay(double widthMillimeters, double heightMillimeters, double thicknessMillimeters)
    {
        return $"{_unitFormattingService.FormatLengthMillimeters(widthMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(heightMillimeters)} × {_unitFormattingService.FormatLengthMillimeters(thicknessMillimeters)}";
    }
}
