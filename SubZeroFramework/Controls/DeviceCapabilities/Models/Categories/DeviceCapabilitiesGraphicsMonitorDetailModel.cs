namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Detail body for a picked monitor, resolved by data navigation on the Graphics category's monitor
/// sub-region (DataViewMap: the picker passes the live card model, so the detail stays bound to the shared page data).</summary>
public sealed class DeviceCapabilitiesGraphicsMonitorDetailModel(DeviceCapabilitiesMonitorCardModel monitor)
{
    public DeviceCapabilitiesMonitorCardModel Monitor { get; } = monitor;
}
