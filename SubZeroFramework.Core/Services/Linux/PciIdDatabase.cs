namespace SubZeroFramework.Services.Linux;

/// <summary>A PCI device to name, as read from sysfs.</summary>
public readonly record struct PciDeviceId(ushort VendorId, ushort DeviceId)
{
    public override string ToString() => $"{VendorId:x4}:{DeviceId:x4}";
}

/// <summary>Names resolved for one device. Either half can be missing when the database has no entry.</summary>
public sealed record PciDeviceNames(string? VendorName, string? DeviceName);

/// <summary>
/// Resolves PCI vendor/device IDs to human names using the system's <c>pci.ids</c> database.
/// </summary>
/// <remarks>
/// The kernel gives us only numeric IDs for a graphics card; the marketing name lives in this text database,
/// shipped by <c>hwdata</c> (Arch, Fedora) or <c>pciutils</c> (Debian, Ubuntu). It is an OPTIONAL dependency:
/// when the file is absent every lookup returns null and callers fall back to a generic name — a GPU listed as
/// "Graphics controller 1002:150e" is worse than "Radeon 890M" but far better than no GPU at all.
///
/// The parse is a single streaming pass filtered to the handful of vendors actually present in the machine, so
/// the 1.6 MB / 42k-line file is never held in memory.
/// </remarks>
public static class PciIdDatabase
{
    /// <summary>
    /// Where distributions put the database, in probe order.
    /// </summary>
    /// <remarks>
    /// hwdata's location is first because Arch and Fedora ship it there; Debian and Ubuntu use the pciutils
    /// location. The <c>/var/lib</c> entry is where <c>update-pciids</c> writes a freshly downloaded copy,
    /// which is newer than the packaged one when present.
    /// </remarks>
    public static IReadOnlyList<string> DefaultSearchPaths { get; } =
    [
        "/usr/share/hwdata/pci.ids",
        "/usr/share/misc/pci.ids",
        "/usr/share/pci.ids",
        "/var/lib/pciutils/pci.ids",
    ];

    /// <summary>First database path that exists, or null when the system has none installed.</summary>
    public static string? FindDatabasePath(IEnumerable<string>? searchPaths = null)
    {
        foreach (var path in searchPaths ?? DefaultSearchPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable candidate is not an error; try the next location.
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the requested devices from the system database. Returns an empty map when no database is
    /// installed — never throws, because a missing optional package must not break hardware enumeration.
    /// </summary>
    public static IReadOnlyDictionary<PciDeviceId, PciDeviceNames> Lookup(
        IReadOnlyCollection<PciDeviceId> devices,
        IEnumerable<string>? searchPaths = null)
    {
        if (devices.Count == 0)
        {
            return new Dictionary<PciDeviceId, PciDeviceNames>();
        }

        var path = FindDatabasePath(searchPaths);
        if (path is null)
        {
            return new Dictionary<PciDeviceId, PciDeviceNames>();
        }

        try
        {
            using var reader = new StreamReader(path);
            return Parse(reader, devices);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<PciDeviceId, PciDeviceNames>();
        }
    }

    /// <summary>
    /// Parses the database from any reader, resolving only the requested devices.
    /// </summary>
    /// <remarks>
    /// Format (verified against hwdata 2026.07.01): comments start with '#'. A vendor is four hex digits at
    /// column 0 followed by two spaces and the name. A device is the same indented by one tab. Subsystems are
    /// indented by two tabs and hold two IDs; they are skipped here. A line beginning "C " starts the device
    /// class section at the end of the file, after which nothing is a vendor.
    /// </remarks>
    public static IReadOnlyDictionary<PciDeviceId, PciDeviceNames> Parse(TextReader reader, IReadOnlyCollection<PciDeviceId> devices)
    {
        Dictionary<PciDeviceId, PciDeviceNames> results = [];
        if (devices.Count == 0)
        {
            return results;
        }

        // Vendors we care about, and the device IDs wanted within each.
        Dictionary<ushort, HashSet<ushort>> wanted = [];
        foreach (var device in devices)
        {
            if (!wanted.TryGetValue(device.VendorId, out var deviceIds))
            {
                deviceIds = [];
                wanted[device.VendorId] = deviceIds;
            }

            deviceIds.Add(device.DeviceId);
        }

        // Vendor names are recorded on sight, independently of whether any of that vendor's devices matched:
        // a GPU newer than the installed database still deserves "Advanced Micro Devices, Inc." as a name.
        Dictionary<ushort, string?> vendorNames = [];

        var remainingVendors = wanted.Count;
        ushort currentVendor = 0;
        string? currentVendorName = null;
        var insideWantedVendor = false;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            // The class section ("C 03  Display controller") ends the vendor list.
            if (line[0] == 'C' && line.Length > 1 && line[1] == ' ')
            {
                break;
            }

            if (line[0] != '\t')
            {
                // Vendor line. Leaving a wanted vendor behind means its devices are all resolved or absent.
                if (insideWantedVendor && --remainingVendors == 0)
                {
                    break;
                }

                insideWantedVendor = TryParseId(line, 0, out currentVendor) && wanted.ContainsKey(currentVendor);
                currentVendorName = insideWantedVendor ? ReadName(line, 4) : null;
                if (insideWantedVendor)
                {
                    vendorNames[currentVendor] = currentVendorName;
                }

                continue;
            }

            if (!insideWantedVendor || line.Length < 2 || line[1] == '\t')
            {
                // Subsystem lines (two tabs) carry board-specific names; the device name is what we show.
                continue;
            }

            if (!TryParseId(line, 1, out var deviceId) || !wanted[currentVendor].Contains(deviceId))
            {
                continue;
            }

            results[new PciDeviceId(currentVendor, deviceId)] = new PciDeviceNames(currentVendorName, ReadName(line, 5));
        }

        // A device the database has never heard of still gets its vendor's name.
        foreach (var device in devices)
        {
            if (!results.ContainsKey(device) && vendorNames.TryGetValue(device.VendorId, out var vendorName) && vendorName is not null)
            {
                results[device] = new PciDeviceNames(vendorName, null);
            }
        }

        return results;
    }

    private static bool TryParseId(string line, int offset, out ushort id)
    {
        id = 0;
        if (line.Length < offset + 4)
        {
            return false;
        }

        return ushort.TryParse(
            line.AsSpan(offset, 4),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out id);
    }

    private static string? ReadName(string line, int idEndOffset)
    {
        // Two spaces separate the ID from the name.
        if (line.Length <= idEndOffset + 2)
        {
            return null;
        }

        var name = line[(idEndOffset + 2)..].Trim();
        return name.Length == 0 ? null : name;
    }
}
