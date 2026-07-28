namespace SubZeroFramework.Models;

/// <summary>
/// A neural processor (or other compute accelerator) as inventory, alongside the CPU and graphics lists.
/// </summary>
/// <remarks>
/// The NPU used to appear only as a utilization percentage with a name, which is far less than the page shows
/// for a CPU package or a graphics adapter. This carries the identity to sit beside the reading. Every field
/// beyond the name is optional, because what a platform can say about an NPU varies enormously: Windows names
/// it and gives a driver version, while Linux can add the kernel module and often a firmware version, and a
/// brand-new driver may offer little more than a PCI address.
/// </remarks>
public sealed record HardwareInfoComputeAccelerator(
    string DeviceKey,
    ComputeDeviceKind Kind,
    string Name,
    string? Vendor,
    string? Description,
    string? DriverName,
    string? DriverVersion,
    string? FirmwareVersion,
    string? Location)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unknown accelerator" : Name;

    public string DisplayKind => Kind switch
    {
        ComputeDeviceKind.Npu => "Neural processor",
        ComputeDeviceKind.Gpu => "Graphics processor",
        _ => "Accelerator",
    };

    public string DisplayVendor => Fallback(Vendor);

    public string DisplayDescription => Fallback(Description);

    public string DisplayDriver => string.IsNullOrWhiteSpace(DriverName)
        ? Fallback(DriverVersion)
        : string.IsNullOrWhiteSpace(DriverVersion)
            ? DriverName
            : $"{DriverName} {DriverVersion}";

    public string DisplayDriverVersion => Fallback(DriverVersion);

    public string DisplayFirmwareVersion => Fallback(FirmwareVersion);

    public string DisplayLocation => Fallback(Location);

    private static string Fallback(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}
