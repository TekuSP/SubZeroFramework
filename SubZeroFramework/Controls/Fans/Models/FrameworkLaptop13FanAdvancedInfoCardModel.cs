using CommunityToolkit.Mvvm.ComponentModel;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop13FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    private readonly IUnitFormattingService _unitFormattingService;

    public FrameworkLaptop13FanAdvancedInfoCardModel(IUnitFormattingService unitFormattingService)
    {
        _unitFormattingService = unitFormattingService;
    }

    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChassisMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproximateFirmwareIdleSpeedDisplay))]
    public partial int ApproximateFirmwareIdleSpeedRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproximateUserTunedIdleSpeedDisplay))]
    public partial int ApproximateUserTunedIdleSpeedRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaximumFirmwareLimitDisplay))]
    public partial int MaximumFirmwareLimitRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproximatePhysicalMaximumDisplay))]
    public partial int ApproximatePhysicalMaximumRpm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApproximateFirmwareIdleSpeedDisplay))]
    [NotifyPropertyChangedFor(nameof(ApproximateUserTunedIdleSpeedDisplay))]
    [NotifyPropertyChangedFor(nameof(MaximumFirmwareLimitDisplay))]
    [NotifyPropertyChangedFor(nameof(ApproximatePhysicalMaximumDisplay))]
    private partial int UnitFormattingRevision { get; set; }

    public string ApproximateFirmwareIdleSpeedDisplay => $"~{_unitFormattingService.FormatFanSpeed(ApproximateFirmwareIdleSpeedRpm)}";

    public string ApproximateUserTunedIdleSpeedDisplay => $"~{_unitFormattingService.FormatFanSpeed(ApproximateUserTunedIdleSpeedRpm)}";

    public string MaximumFirmwareLimitDisplay => _unitFormattingService.FormatFanSpeed(MaximumFirmwareLimitRpm);

    public string ApproximatePhysicalMaximumDisplay => $"~{_unitFormattingService.FormatFanSpeed(ApproximatePhysicalMaximumRpm)}";

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
        UnitFormattingRevision++;
    }
}
