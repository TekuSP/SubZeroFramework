using System.Globalization;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Parses amdgpu's <c>pp_dpm_sclk</c> DPM table, which is how the driver states the core clock steps a card
/// will actually run at.
/// </summary>
/// <remarks>
/// <para>
/// The file is a plain listing, one performance state per line, with an asterisk marking the current one:
/// </para>
/// <code>
/// 0: 500Mhz
/// 1: 1150Mhz *
/// 2: 2200Mhz
/// </code>
/// <para>
/// The highest listed state is the maximum the driver will schedule — the POLICY cap, which is exactly what
/// <see cref="Models.ComputeDeviceUtilization.MaxCoreClockMegahertz"/> is defined as, rather than the silicon
/// limit. Kept separate from the reader and given its own tests because the format varies more than it looks:
/// the unit suffix is spelled "Mhz" on most ASICs but "MHz" on some, spacing is inconsistent, and APUs list
/// fewer states than discrete cards.
/// </para>
/// <para>
/// Deliberately NOT taken from hwmon's <c>freq1_input</c>, which reports the current clock only, nor from the
/// asterisked line, which is the current state and would make "maximum" follow the load around.
/// </para>
/// </remarks>
public static class AmdGpuClockTable
{
    /// <summary>
    /// The highest clock in a <c>pp_dpm_sclk</c> listing, in MHz, or null when nothing parses.
    /// </summary>
    /// <remarks>
    /// Unparseable lines are SKIPPED rather than failing the whole read: a kernel that adds a header or a
    /// trailing note should cost the extra line, not the entire reading.
    /// </remarks>
    public static double? ParseMaximumMegahertz(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        double? maximum = null;

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ParseLineMegahertz(line) is { } megahertz && (maximum is null || megahertz > maximum))
            {
                maximum = megahertz;
            }
        }

        return maximum;
    }

    /// <summary>Reads the clock out of one "<c>1: 2200Mhz *</c>" line.</summary>
    private static double? ParseLineMegahertz(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        var remainder = line[(colon + 1)..].AsSpan().Trim();

        // Take the leading run of digits (and a decimal point, which some ASICs emit) and stop at the unit.
        var length = 0;
        while (length < remainder.Length && (char.IsAsciiDigit(remainder[length]) || remainder[length] == '.'))
        {
            length++;
        }

        if (length == 0)
        {
            return null;
        }

        if (!double.TryParse(remainder[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value <= 0d)
        {
            return null;
        }

        // Require the MHz unit rather than assuming it: a future table in a different unit must be ignored,
        // not silently misread by a factor of a thousand.
        return remainder[length..].TrimStart().StartsWith("mhz", StringComparison.OrdinalIgnoreCase)
            ? value
            : null;
    }
}
