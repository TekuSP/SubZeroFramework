namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Builds a canonical PCI address from the numeric properties Windows exposes for a PnP device.
/// </summary>
/// <remarks>
/// The point is to JOIN a Windows PnP device to NVML, which identifies GPUs by bus address rather than by OS
/// identity. Windows also offers <c>DEVPKEY_Device_LocationInfo</c> — "PCI bus 194, device 0, function 0" —
/// but that string is LOCALIZED, so parsing it would work on an English install and quietly fail elsewhere.
/// The numeric properties carry the same information and mean the same thing in every language.
///
/// Verified against the reference machine: bus 194, address 0 produces "0000:c2:00.0", which is exactly what
/// NVML reports for the same GPU.
/// </remarks>
public static class WindowsPciAddress
{
    /// <summary>
    /// Formats a PCI address from a bus number and the packed <c>DEVPKEY_Device_Address</c> value.
    /// </summary>
    /// <param name="busNumber">The value of <c>DEVPKEY_Device_BusNumber</c>.</param>
    /// <param name="address">
    /// The value of <c>DEVPKEY_Device_Address</c>, which packs the device number in the high 16 bits and the
    /// function number in the low 16.
    /// </param>
    /// <returns>A lower-case address such as <c>0000:c2:00.0</c>, or null when either input is missing.</returns>
    public static string? Format(uint? busNumber, uint? address)
    {
        if (busNumber is not { } bus || address is not { } packed)
        {
            return null;
        }

        var device = (packed >> 16) & 0xFFFF;
        var function = packed & 0xFFFF;

        // Domain is hardcoded to 0000: Windows does not surface a PCI segment through this property, and
        // consumer hardware has exactly one. NVML formats its busIdLegacy the same way, which is what makes
        // the two comparable at all.
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"0000:{bus:x2}:{device:x2}.{function:x}");
    }

    /// <summary>True when two PCI addresses refer to the same device, ignoring case.</summary>
    public static bool Matches(string? left, string? right)
        => !string.IsNullOrEmpty(left)
            && !string.IsNullOrEmpty(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
