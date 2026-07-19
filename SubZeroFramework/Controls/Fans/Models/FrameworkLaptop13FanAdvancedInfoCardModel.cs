using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop13FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkLaptop13FanAdvancedInfoCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
        RefreshUnitFormatting();
    }

    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChassisMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int ApproximateFirmwareIdleSpeedRpm { get; set; }

    partial void OnApproximateFirmwareIdleSpeedRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int ApproximateUserTunedIdleSpeedRpm { get; set; }

    partial void OnApproximateUserTunedIdleSpeedRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int MaximumFirmwareLimitRpm { get; set; }

    partial void OnMaximumFirmwareLimitRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial int ApproximatePhysicalMaximumRpm { get; set; }

    partial void OnApproximatePhysicalMaximumRpmChanged(int value) => RefreshUnitFormatting();

    [ObservableProperty]
    public partial string ApproximateFirmwareIdleSpeedDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ApproximateUserTunedIdleSpeedDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MaximumFirmwareLimitDisplay { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ApproximatePhysicalMaximumDisplay { get; private set; } = string.Empty;

    public void UpdateFrom(FrameworkLaptop13CoolingDetails details)
    {
        ProcessorSupport = details.ProcessorSupport;
        ChassisMaterial = details.ChassisMaterial;
        ApproximateFirmwareIdleSpeedRpm = details.ApproximateFirmwareIdleSpeedRpm;
        ApproximateUserTunedIdleSpeedRpm = details.ApproximateUserTunedIdleSpeedRpm;
        MaximumFirmwareLimitRpm = details.MaximumFirmwareLimitRpm;
        ApproximatePhysicalMaximumRpm = details.ApproximatePhysicalMaximumRpm;
    }

    public override void RefreshUnitFormatting()
    {
        ApproximateFirmwareIdleSpeedDisplay = $"~{_unitFormattingService.FormatFanSpeed(ApproximateFirmwareIdleSpeedRpm)}";
        ApproximateUserTunedIdleSpeedDisplay = $"~{_unitFormattingService.FormatFanSpeed(ApproximateUserTunedIdleSpeedRpm)}";
        MaximumFirmwareLimitDisplay = _unitFormattingService.FormatFanSpeed(MaximumFirmwareLimitRpm);
        ApproximatePhysicalMaximumDisplay = $"~{_unitFormattingService.FormatFanSpeed(ApproximatePhysicalMaximumRpm)}";
    }
}
