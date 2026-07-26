namespace SubZeroFramework.Services.Linux;

/// <summary>A DRM connector directory name split into its parts.</summary>
public readonly record struct DrmConnectorName(string CardName, string ConnectorType, int ConnectorIndex)
{
    /// <summary>The label a user would recognise from a display setting panel, e.g. "eDP-1", "HDMI-A-2".</summary>
    public string DisplayName => $"{ConnectorType}-{ConnectorIndex}";

    /// <summary>True for the panel types that are physically part of a laptop.</summary>
    public bool IsInternalPanel =>
        ConnectorType.Equals("eDP", StringComparison.OrdinalIgnoreCase)
        || ConnectorType.Equals("LVDS", StringComparison.OrdinalIgnoreCase)
        || ConnectorType.Equals("DSI", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Splits "card0-HDMI-A-1" into card "card0", type "HDMI-A" and index 1.
    /// </summary>
    /// <remarks>
    /// The connector type may itself contain dashes ("HDMI-A", "DVI-I"), so the index is taken from the LAST
    /// dash-separated segment rather than the second — splitting on the first dash yields a type of "HDMI"
    /// and an index parse failure on the most common external connector there is.
    /// </remarks>
    public static bool TryParse(string directoryName, out DrmConnectorName connector)
    {
        connector = default;

        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        var cardSeparator = directoryName.IndexOf('-');
        if (cardSeparator <= 0 || cardSeparator == directoryName.Length - 1)
        {
            return false;
        }

        var cardName = directoryName[..cardSeparator];
        if (!DrmSysfs.IsCardName(cardName))
        {
            return false;
        }

        var remainder = directoryName[(cardSeparator + 1)..];
        var indexSeparator = remainder.LastIndexOf('-');
        if (indexSeparator <= 0 || indexSeparator == remainder.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(remainder.AsSpan(indexSeparator + 1), out var index))
        {
            return false;
        }

        connector = new DrmConnectorName(cardName, remainder[..indexSeparator], index);
        return true;
    }
}

/// <summary>Connection state of a DRM connector, as the <c>status</c> attribute reports it.</summary>
public enum DrmConnectorStatus
{
    /// <summary>The driver cannot tell — treated as not connected for display purposes.</summary>
    Unknown,
    Connected,
    Disconnected,
}

public static class DrmConnectorStatusParser
{
    /// <summary>Parses the verbatim contents of a connector's <c>status</c> attribute.</summary>
    public static DrmConnectorStatus Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "connected" => DrmConnectorStatus.Connected,
        "disconnected" => DrmConnectorStatus.Disconnected,
        _ => DrmConnectorStatus.Unknown,
    };
}

/// <summary>One entry from a connector's <c>modes</c> attribute.</summary>
public readonly record struct DrmMode(int Width, int Height, bool Interlaced)
{
    /// <summary>
    /// Parses a mode line. The kernel prints the mode NAME only — "2560x1600", or "1920x1080i" when
    /// interlaced. There is deliberately no refresh rate here; that has to come from the EDID timing.
    /// </summary>
    public static bool TryParse(string? line, out DrmMode mode)
    {
        mode = default;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var text = line.Trim();
        var interlaced = text.EndsWith('i');
        if (interlaced)
        {
            text = text[..^1];
        }

        var separator = text.IndexOf('x');
        if (separator <= 0 || separator == text.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(text.AsSpan(..separator), out var width)
            || !int.TryParse(text.AsSpan(separator + 1), out var height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        mode = new DrmMode(width, height, interlaced);
        return true;
    }

    /// <summary>
    /// Parses a whole <c>modes</c> file. The first entry is the connector's preferred mode.
    /// </summary>
    public static IReadOnlyList<DrmMode> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<DrmMode> modes = [];
        foreach (var line in text.Split('\n'))
        {
            if (TryParse(line, out var mode))
            {
                modes.Add(mode);
            }
        }

        return modes;
    }
}
