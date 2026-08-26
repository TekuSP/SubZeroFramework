namespace SubZeroFramework.Models;

/// <summary>
/// The arithmetic behind reading CPU package power from Linux's RAPL powercap counters.
/// </summary>
/// <remarks>
/// Pure and separate from the reader that uses it for two reasons. The wrap handling is the subtle part and
/// deserves direct tests rather than tests mediated by a filesystem; and the real zone directories are named
/// <c>intel-rapl:0</c>, which cannot be created on NTFS, so a filesystem-shaped test of this logic could only
/// ever run on Linux.
/// </remarks>
public static class RaplEnergyMath
{
    /// <summary>
    /// The powercap control types that expose a RAPL package zone.
    /// </summary>
    /// <remarks>
    /// Both, because the name depends on which driver claimed the part. AMD systems have historically appeared
    /// under <c>intel-rapl</c> — the powercap control type kept its original name when
    /// <c>intel_rapl_common</c> grew AMD support — while newer kernels register <c>amd-rapl</c>. Matching only
    /// the Intel spelling leaves an AMD machine with no package power on exactly the kernels where it does
    /// work, which on a Framework 16 is most of them.
    /// </remarks>
    public static readonly string[] PackageZonePrefixes = ["intel-rapl:", "amd-rapl:"];

    /// <summary>
    /// True for a top-level package zone such as <c>intel-rapl:0</c> or <c>amd-rapl:0</c>, false for a nested
    /// subzone such as <c>intel-rapl:0:0</c>.
    /// </summary>
    /// <remarks>
    /// The subzones are core / uncore / dram slices of the SAME package budget, so counting one as the package
    /// total reports a fraction of the truth, and summing them alongside the package double counts.
    /// </remarks>
    public static bool IsPackageZoneName(string zoneName)
        => !string.IsNullOrEmpty(zoneName)
            && PackageZonePrefixes.Any(prefix => zoneName.StartsWith(prefix, StringComparison.Ordinal))
            && zoneName.Count(character => character == ':') == 1;

    /// <summary>
    /// The value in a zone's <c>name</c> file that identifies it as the integrated GPU's power plane.
    /// </summary>
    /// <remarks>
    /// This is RAPL's PP1 domain. The powercap driver spells it "uncore"
    /// (<c>rapl_domain_names[RAPL_DOMAIN_PP1]</c> in <c>intel_rapl_common.c</c>), but the kernel's own
    /// documentation in that file names the matching perf event <c>energy_gpu</c> — PP1 IS the graphics
    /// plane on client parts, which is why it is the one route to Intel iGPU power on Linux.
    ///
    /// Matched on the <c>name</c> file rather than the directory index: whether the GPU plane lands at
    /// <c>intel-rapl:0:0</c> or <c>intel-rapl:0:1</c> depends on which domains the part exposes, so keying on
    /// the index would read the core or dram plane on some machines.
    /// </remarks>
    public const string GpuDomainName = "uncore";

    /// <summary>
    /// True for a nested subzone such as <c>intel-rapl:0:1</c>, false for a top-level package zone.
    /// </summary>
    public static bool IsSubzoneName(string zoneName)
        => !string.IsNullOrEmpty(zoneName)
            && zoneName.StartsWith("intel-rapl:", StringComparison.Ordinal)
            && zoneName.Count(character => character == ':') == 2;

    /// <summary>
    /// Average power over the window between two energy readings, or null when the pair cannot yield one.
    /// </summary>
    /// <param name="previousMicrojoules">The energy counter at the start of the window.</param>
    /// <param name="currentMicrojoules">The energy counter at the end of the window.</param>
    /// <param name="elapsedSeconds">Length of the window. Must be positive.</param>
    /// <param name="rangeMicrojoules">
    /// The counter's wrap point (<c>max_energy_range_uj</c>), or null when it could not be read. A wrap
    /// without a known range yields null rather than a guess: on a laptop the counter wraps every few minutes,
    /// so a plausible-looking wrong figure would be produced repeatedly and silently.
    /// </param>
    public static double? ComputeWatts(
        long previousMicrojoules,
        long currentMicrojoules,
        double elapsedSeconds,
        double? rangeMicrojoules)
    {
        if (elapsedSeconds <= 0d || double.IsNaN(elapsedSeconds))
        {
            return null;
        }

        var deltaMicrojoules = (double)(currentMicrojoules - previousMicrojoules);

        if (deltaMicrojoules < 0d)
        {
            if (rangeMicrojoules is not { } range || range <= 0d)
            {
                return null;
            }

            deltaMicrojoules += range;

            // Still negative means the counter moved further back than one wrap explains — a reset, a zone
            // swapped underneath us, or a bad read. None of those is a power figure.
            if (deltaMicrojoules < 0d)
            {
                return null;
            }
        }

        return deltaMicrojoules / 1_000_000d / elapsedSeconds;
    }
}
