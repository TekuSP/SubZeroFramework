using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.Fans.Models;

public partial class FrameworkLaptop13FanAdvancedInfoCardModel : FanAdvancedInfoCardModel
{
    [ObservableProperty]
    public partial string ProcessorSupport { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChassisMaterial { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int ApproximateFirmwareIdleSpeedRpm { get; set; }

    [ObservableProperty]
    public partial int ApproximateUserTunedIdleSpeedRpm { get; set; }

    [ObservableProperty]
    public partial int MaximumFirmwareLimitRpm { get; set; }

    [ObservableProperty]
    public partial int ApproximatePhysicalMaximumRpm { get; set; }

    public void UpdateFrom(FrameworkLaptop13CoolingDetails details)
    {
        ProcessorSupport = details.ProcessorSupport;
        ChassisMaterial = details.ChassisMaterial;
        ApproximateFirmwareIdleSpeedRpm = details.ApproximateFirmwareIdleSpeedRpm;
        ApproximateUserTunedIdleSpeedRpm = details.ApproximateUserTunedIdleSpeedRpm;
        MaximumFirmwareLimitRpm = details.MaximumFirmwareLimitRpm;
        ApproximatePhysicalMaximumRpm = details.ApproximatePhysicalMaximumRpm;
    }
}
