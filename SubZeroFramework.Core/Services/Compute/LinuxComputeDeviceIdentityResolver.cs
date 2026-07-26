using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Describes the machine's neural processors from sysfs, for the Neural processor detail pane.
/// </summary>
/// <remarks>
/// Only NPUs are enumerated. GPUs already reach the UI through the DRM graphics inventory, and describing
/// them here as well would list the same hardware twice under two names.
///
/// Everything is read from the accel class and the backing PCI device — no device node is opened. That is a
/// deliberate constraint rather than an optimisation: closing an ivpu accel handle forces a runtime resume,
/// so a resolver that opened one would wake the NPU every inventory refresh just to read its name.
/// </remarks>
public sealed class LinuxComputeDeviceIdentityResolver(
    ILogger<LinuxComputeDeviceIdentityResolver> logger,
    string sysfsRoot = DrmSysfs.DefaultSysfsRoot) : IComputeDeviceIdentityResolver
{
    private readonly LinuxAccelSysfs _accel = new(sysfsRoot);
    private bool _loggedFailure;

    public IReadOnlyList<ComputeDeviceIdentity> Enumerate()
    {
        try
        {
            var devices = _accel.EnumerateDevices();
            if (devices.Count == 0)
            {
                return [];
            }

            var pciNames = PciIdDatabase.Lookup(
                [.. devices
                    .Where(device => device.VendorId is not null && device.DeviceId is not null)
                    .Select(device => new PciDeviceId(device.VendorId!.Value, device.DeviceId!.Value))
                    .Distinct()]);

            List<ComputeDeviceIdentity> identities = [];
            foreach (var device in devices)
            {
                PciDeviceNames? names = null;
                if (device.VendorId is not null && device.DeviceId is not null)
                {
                    pciNames.TryGetValue(new PciDeviceId(device.VendorId.Value, device.DeviceId.Value), out names);
                }

                identities.Add(new ComputeDeviceIdentity
                {
                    DeviceKey = device.DeviceKey,
                    Kind = ComputeDeviceKind.Npu,
                    DisplayName = names?.DeviceName ?? device.DisplayName,
                    Vendor = names?.VendorName,
                    Description = BuildDescription(device),
                    DriverName = device.Driver,
                    DriverVersion = ReadDriverVersion(device),
                    FirmwareVersion = ReadFirmwareVersion(device),
                    Location = device.PciSlotName,
                });
            }

            return identities;
        }
        catch (Exception exception)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                logger.LogWarning(exception, "Could not enumerate neural processors from {AccelPath}.", _accel.ClassAccelPath);
            }

            return [];
        }
    }

    private static string? BuildDescription(LinuxAccelDevice device)
    {
        // amdxdna names the silicon revision in a "vbnv" attribute ("RyzenAI-npu4"), which is more specific
        // than anything pci.ids carries for these parts.
        var boardName = DrmSysfs.ReadAttribute(Path.Combine(device.DevicePath, "vbnv"));
        if (!string.IsNullOrWhiteSpace(boardName))
        {
            return boardName;
        }

        var deviceType = DrmSysfs.ReadAttribute(Path.Combine(device.DevicePath, "device_type"));
        return string.IsNullOrWhiteSpace(deviceType) ? null : deviceType;
    }

    private static string? ReadDriverVersion(LinuxAccelDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Driver))
        {
            return null;
        }

        var version = DrmSysfs.ReadAttribute(Path.Combine("/sys", "module", device.Driver, "version"));
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    /// <summary>
    /// NPU firmware version, where the driver publishes one in sysfs.
    /// </summary>
    /// <remarks>
    /// amdxdna exposes <c>fw_version</c>. ivpu does not: its firmware version string is reachable only
    /// through debugfs or by parsing the firmware blob, neither of which is worth doing from a service, so
    /// Intel NPUs report an unknown firmware rather than a guessed one.
    /// </remarks>
    private static string? ReadFirmwareVersion(LinuxAccelDevice device)
    {
        var firmware = DrmSysfs.ReadAttribute(Path.Combine(device.DevicePath, "fw_version"));
        return string.IsNullOrWhiteSpace(firmware) ? null : firmware;
    }
}
