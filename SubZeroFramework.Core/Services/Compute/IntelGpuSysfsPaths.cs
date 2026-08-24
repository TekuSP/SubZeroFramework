namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Where Intel graphics drivers put the current-clock attribute in sysfs.
/// </summary>
/// <remarks>
/// Extracted from the reader so it can be tested off Linux. The reader itself cannot be: its devices only
/// exist once <c>perf_event_open</c> has succeeded against a real PMU, so nothing reaches the frequency code
/// on a machine without one.
///
/// The two drivers disagree about the layout, and a Framework 13 can be running either depending on kernel
/// and generation, so both are handled rather than picking the newer one.
/// </remarks>
public static class IntelGpuSysfsPaths
{
    /// <summary>The xe driver name, as it appears in a DRM uevent.</summary>
    public const string XeDriverName = "xe";

    /// <summary>
    /// Turns a PMU directory name into the PCI address it refers to, or null for an integrated GPU.
    /// </summary>
    /// <remarks>
    /// i915 names the PMU <c>i915</c> for the integrated GPU and <c>i915_0000_03_00.0</c> for a discrete one;
    /// xe always carries the address. The underscores stand in for colons because a PMU name cannot contain
    /// them.
    /// </remarks>
    public static string? ExtractBusAddress(string pmuDirectoryName)
    {
        if (string.IsNullOrEmpty(pmuDirectoryName))
        {
            return null;
        }

        var underscore = pmuDirectoryName.IndexOf('_');
        return underscore > 0 ? pmuDirectoryName[(underscore + 1)..].Replace('_', ':') : null;
    }

    /// <summary>
    /// The current-clock attribute for a card, given its DRM card path and its device path.
    /// </summary>
    /// <remarks>
    /// i915 exposes <c>gt_cur_freq_mhz</c> on the CARD directory; xe moved to a per-tile, per-GT tree under
    /// the DEVICE directory. Passing the wrong one of the two directories is the easy mistake here, which is
    /// why both are parameters rather than being derived from each other inside.
    /// </remarks>
    public static string GetFrequencyAttributePath(string cardPath, string devicePath, string driverName)
        => string.Equals(driverName, XeDriverName, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(devicePath, "tile0", "gt0", "freq0", "cur_freq")
            : Path.Combine(cardPath, "gt_cur_freq_mhz");

    /// <summary>
    /// The highest clock the driver will currently allow, which the current clock is measured against.
    /// </summary>
    /// <remarks>
    /// Deliberately the POLICY cap (<c>gt_max_freq_mhz</c> / <c>max_freq</c>) rather than the silicon ceiling
    /// (<c>gt_RP0_freq_mhz</c> / <c>rp0_freq</c>). A part held below its silicon maximum by a deliberate power
    /// policy is not throttling in any sense worth spinning fans up over.
    /// </remarks>
    public static string GetMaximumFrequencyAttributePath(string cardPath, string devicePath, string driverName)
        => string.Equals(driverName, XeDriverName, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(devicePath, "tile0", "gt0", "freq0", "max_freq")
            : Path.Combine(cardPath, "gt_max_freq_mhz");

    /// <summary>
    /// The directory holding the per-reason throttle attributes, and the prefix their filenames carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// i915 puts <c>throttle_reason_*</c> directly in the GT directory (<c>card0/gt/gt0/</c>); xe puts
    /// <c>reason_*</c> in a dedicated <c>freq0/throttle/</c> directory under the device. The prefix travels
    /// with the directory because the two are not independently chosen.
    /// </para>
    /// <para>
    /// Unlike the hwmon attributes, these are NOT discrete-only. i915 registers them whenever
    /// <c>intel_gt_perf_limit_reasons_reg()</c> is valid — GRAPHICS_VER >= 11 — so every Intel Framework
    /// (Tiger Lake is Gen12) reports them.
    /// </para>
    /// </remarks>
    public static (string Directory, string Prefix) GetThrottleReasonLocation(string cardPath, string devicePath, string driverName)
        => string.Equals(driverName, XeDriverName, StringComparison.OrdinalIgnoreCase)
            ? (Path.Combine(devicePath, "tile0", "gt0", "freq0", "throttle"), IntelGpuThrottleReasons.XeAttributePrefix)
            : (Path.Combine(cardPath, "gt", "gt0"), IntelGpuThrottleReasons.I915AttributePrefix);
}
