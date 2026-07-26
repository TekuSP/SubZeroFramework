namespace SubZeroFramework.Services.Linux;

/// <summary>
/// The key/value pairs the kernel exposes in a device's <c>uevent</c> file.
/// </summary>
/// <remarks>
/// Preferred over resolving the <c>device</c> symlink for identity: <c>PCI_SLOT_NAME</c> is the same
/// bus address the symlink target encodes, but reading it is a plain file read with no link traversal, and the
/// same file also carries <c>DRIVER</c>, which tells amdgpu / i915 / xe / nvidia apart — the decision that
/// selects a utilization source.
/// </remarks>
public sealed record DrmUevent
{
    /// <summary>Kernel driver bound to the device: "amdgpu", "i915", "xe", "nvidia", "nouveau".</summary>
    public string? Driver { get; init; }

    /// <summary>PCI bus address in "0000:c1:00.0" form — stable across reboots, so it is the device key.</summary>
    public string? PciSlotName { get; init; }

    /// <summary>"0x1002" style vendor:device pair from PCI_ID, when present.</summary>
    public ushort? VendorId { get; init; }

    public ushort? DeviceId { get; init; }

    /// <summary>
    /// Parses uevent text. Format is one KEY=VALUE per line, e.g.
    /// <code>
    /// DRIVER=amdgpu
    /// PCI_CLASS=30000
    /// PCI_ID=1002:150E
    /// PCI_SLOT_NAME=0000:c1:00.0
    /// </code>
    /// </summary>
    public static DrmUevent Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DrmUevent();
        }

        string? driver = null;
        string? pciSlotName = null;
        ushort? vendorId = null;
        ushort? deviceId = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];

            switch (key)
            {
                case "DRIVER":
                    driver = value.Length == 0 ? null : value;
                    break;
                case "PCI_SLOT_NAME":
                    pciSlotName = value.Length == 0 ? null : value;
                    break;
                case "PCI_ID":
                {
                    // "1002:150E" — bare hex, no 0x prefix, unlike the vendor/device attribute files.
                    var colon = value.IndexOf(':');
                    if (colon > 0)
                    {
                        vendorId = ParseBareHex(value[..colon]);
                        deviceId = ParseBareHex(value[(colon + 1)..]);
                    }

                    break;
                }
            }
        }

        return new DrmUevent
        {
            Driver = driver,
            PciSlotName = pciSlotName,
            VendorId = vendorId,
            DeviceId = deviceId,
        };
    }

    private static ushort? ParseBareHex(string text) =>
        ushort.TryParse(
            text.AsSpan().Trim(),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
}
