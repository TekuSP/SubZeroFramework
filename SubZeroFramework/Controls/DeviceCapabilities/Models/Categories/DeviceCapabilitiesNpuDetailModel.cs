namespace SubZeroFramework.Controls.DeviceCapabilities.Models.Categories;

/// <summary>Detail body for a picked neural processor, resolved by data navigation on the category's inner
/// sub-region (DataViewMap: the picker passes the live card model, so the detail stays bound to the shared page data).</summary>
public sealed class DeviceCapabilitiesNpuDetailModel(ComputeDeviceUsageCardModel device)
{
    public ComputeDeviceUsageCardModel Device { get; } = device;
}
