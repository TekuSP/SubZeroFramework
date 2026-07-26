using System.Globalization;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Low-level reads of the kernel's DRM sysfs tree.
/// </summary>
/// <remarks>
/// Everything here works on a headless root service: <c>/sys/class/drm</c> is populated by the kernel driver,
/// not by a display server, so it is identical under X11, Wayland and no session at all. That is why this
/// exists — Hardware.Info implements the same lists by shelling out to <c>xrandr</c>, which cannot work here.
///
/// The sysfs root is injectable so the enumeration logic can be tested against fixture trees instead of real
/// hardware. Every read is failure-tolerant: sysfs attributes appear and vanish with driver state, and a
/// missing or unreadable file must degrade the result, never throw out of a telemetry tick.
/// </remarks>
public sealed class DrmSysfs(string sysfsRoot = DrmSysfs.DefaultSysfsRoot)
{
    public const string DefaultSysfsRoot = "/sys";

    public string ClassDrmPath { get; } = Path.Combine(sysfsRoot, "class", "drm");

    /// <summary>
    /// Card directory names (<c>card0</c>, <c>card1</c>), excluding render nodes and connector entries.
    /// </summary>
    /// <remarks>
    /// <c>/sys/class/drm</c> mixes three kinds of entry: cards (<c>card0</c>), per-card connectors
    /// (<c>card0-eDP-1</c>) and render nodes (<c>renderD128</c>). Only the first is a GPU.
    /// </remarks>
    public IReadOnlyList<string> EnumerateCardNames()
    {
        try
        {
            if (!Directory.Exists(ClassDrmPath))
            {
                return [];
            }

            List<string> cards = [];
            foreach (var directory in Directory.EnumerateDirectories(ClassDrmPath))
            {
                var name = Path.GetFileName(directory);
                if (IsCardName(name))
                {
                    cards.Add(name);
                }
            }

            cards.Sort(StringComparer.Ordinal);
            return cards;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>True for "card0" and not for "card0-eDP-1" or "renderD128".</summary>
    public static bool IsCardName(string name) =>
        name.StartsWith("card", StringComparison.Ordinal)
        && name.Length > 4
        && name.AsSpan(4).IndexOf('-') < 0
        && name.AsSpan(4).ToString().All(char.IsAsciiDigit);

    /// <summary>Connector directory names belonging to a card, e.g. <c>card0-eDP-1</c>.</summary>
    public IReadOnlyList<string> EnumerateConnectorNames(string cardName)
    {
        try
        {
            if (!Directory.Exists(ClassDrmPath))
            {
                return [];
            }

            var prefix = cardName + "-";
            List<string> connectors = [];
            foreach (var directory in Directory.EnumerateDirectories(ClassDrmPath))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    connectors.Add(name);
                }
            }

            connectors.Sort(StringComparer.Ordinal);
            return connectors;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string GetCardPath(string cardName) => Path.Combine(ClassDrmPath, cardName);

    /// <summary>The card's PCI device directory, where vendor/device IDs and driver attributes live.</summary>
    public string GetCardDevicePath(string cardName) => Path.Combine(ClassDrmPath, cardName, "device");

    public string GetConnectorPath(string connectorName) => Path.Combine(ClassDrmPath, connectorName);

    /// <summary>Reads a sysfs attribute, trimmed. Null when absent or unreadable.</summary>
    public static string? ReadAttribute(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Attributes routinely fail with -EINVAL / -EBUSY depending on driver state (a powered-down GPU
            // is the common case); an unreadable attribute means "unknown", not "broken".
            return null;
        }
    }

    /// <summary>Reads an attribute holding a plain decimal integer.</summary>
    public static long? ReadInt64Attribute(string path)
    {
        var text = ReadAttribute(path);
        return text is not null && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Reads an attribute holding a "0x1002"-style hex ID, as the PCI vendor/device files do.</summary>
    public static ushort? ReadHexIdAttribute(string path)
    {
        var text = ReadAttribute(path);
        return ParseHexId(text);
    }

    /// <summary>Parses the "0x1002" form sysfs uses for PCI IDs.</summary>
    public static ushort? ParseHexId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var span = text.AsSpan().Trim();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        return ushort.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Reads a device's raw EDID blob. Null when the file is absent or empty (nothing connected).</summary>
    public static byte[]? ReadEdid(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return bytes.Length == 0 ? null : bytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
