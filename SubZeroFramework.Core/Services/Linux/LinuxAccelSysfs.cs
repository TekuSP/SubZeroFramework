namespace SubZeroFramework.Services.Linux;

/// <summary>One compute-accelerator device as the kernel's accel class describes it.</summary>
public sealed record LinuxAccelDevice
{
    /// <summary>The accel class node name, e.g. "accel0".</summary>
    public required string NodeName { get; init; }

    /// <summary>The backing PCI device directory, where the driver's own attributes live.</summary>
    public required string DevicePath { get; init; }

    /// <summary>Kernel module bound to it: "amdxdna", "intel_vpu".</summary>
    public string? Driver { get; init; }

    /// <summary>PCI bus address; stable across reboots, so it is the telemetry device key.</summary>
    public string? PciSlotName { get; init; }

    public ushort? VendorId { get; init; }

    public ushort? DeviceId { get; init; }

    /// <summary>Stable identity, falling back to the node name when the device has no bus address.</summary>
    public string DeviceKey => PciSlotName ?? NodeName;

    /// <summary>Placeholder name; callers replace it with a pci.ids lookup where one is available.</summary>
    public string DisplayName => Driver is null
        ? $"Neural processor ({NodeName})"
        : $"{Driver} NPU ({DeviceKey})";
}

/// <summary>
/// Enumerates <c>/sys/class/accel</c>, the kernel's device class for compute accelerators.
/// </summary>
/// <remarks>
/// NPUs live here rather than under <c>/sys/class/drm</c>: the accel subsystem was split out of DRM precisely
/// because these devices render nothing. The layout mirrors DRM otherwise — a class node whose <c>device</c>
/// symlink points at the PCI device that carries the driver's attributes — so this is the accel counterpart
/// of <see cref="DrmSysfs"/>, with the same injectable root so it can be tested against fixture trees.
/// </remarks>
public sealed class LinuxAccelSysfs(string sysfsRoot = DrmSysfs.DefaultSysfsRoot)
{
    public string ClassAccelPath { get; } = Path.Combine(sysfsRoot, "class", "accel");

    /// <summary>Every accel device, or empty when the class does not exist (no NPU, or a kernel without it).</summary>
    public IReadOnlyList<LinuxAccelDevice> EnumerateDevices()
    {
        try
        {
            if (!Directory.Exists(ClassAccelPath))
            {
                return [];
            }

            List<LinuxAccelDevice> devices = [];
            foreach (var directory in Directory.EnumerateDirectories(ClassAccelPath).OrderBy(path => path, StringComparer.Ordinal))
            {
                var nodeName = Path.GetFileName(directory);
                if (!nodeName.StartsWith("accel", StringComparison.Ordinal))
                {
                    continue;
                }

                var devicePath = Path.Combine(directory, "device");
                var uevent = DrmUevent.Parse(DrmSysfs.ReadAttribute(Path.Combine(devicePath, "uevent")));

                devices.Add(new LinuxAccelDevice
                {
                    NodeName = nodeName,
                    DevicePath = devicePath,
                    Driver = uevent.Driver,
                    PciSlotName = uevent.PciSlotName,
                    VendorId = uevent.VendorId ?? DrmSysfs.ReadHexIdAttribute(Path.Combine(devicePath, "vendor")),
                    DeviceId = uevent.DeviceId ?? DrmSysfs.ReadHexIdAttribute(Path.Combine(devicePath, "device")),
                });
            }

            return devices;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
