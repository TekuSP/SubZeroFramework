using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Maps the Intel GPU throttle-reason sysfs attributes onto the vendor-neutral flags.
/// </summary>
/// <remarks>
/// <para>
/// Both drivers expose a set of boolean attributes, one per reason, and the names differ:
/// i915 spells them <c>throttle_reason_thermal</c> under the GT directory, xe spells them
/// <c>reason_thermal</c> under <c>freq0/throttle/</c>. The stem after the prefix is the same vocabulary, so
/// only the stem is matched here.
/// </para>
/// <para>
/// This is NOT gated to discrete parts, unlike Intel's hwmon: i915 registers the attributes whenever
/// <c>intel_gt_perf_limit_reasons_reg()</c> is valid, which is GRAPHICS_VER >= 11 — every Intel Framework
/// qualifies (Tiger Lake is Gen12). So throttle state is the one extended signal an Intel iGPU on Linux CAN
/// report, and it is the most useful one for fan control.
/// </para>
/// </remarks>
public static class IntelGpuThrottleReasons
{
    /// <summary>The i915 attribute prefix; the file lives directly in the GT directory.</summary>
    public const string I915AttributePrefix = "throttle_reason_";

    /// <summary>The xe attribute prefix; the file lives in the <c>freq0/throttle/</c> directory.</summary>
    public const string XeAttributePrefix = "reason_";

    /// <summary>
    /// The reason stems this app reads, in the union of both drivers' vocabularies.
    /// </summary>
    /// <remarks>
    /// Deliberately a fixed list rather than a directory scan: a reason nobody has mapped would otherwise be
    /// silently ignored, whereas a missing file here simply reads as "not throttling for that reason".
    /// </remarks>
    public static IReadOnlyList<string> ReasonStems { get; } =
    [
        // Package power limits — both drivers.
        "pl1", "pl2", "pl4",
        // Platform (PSYS) power limits — Crescent Island and later on xe.
        "psys_pl1", "psys_pl2",
        // Thermal, in every spelling either driver uses.
        "thermal", "soc_thermal", "mem_thermal", "vr_thermal", "vr_thermalert", "soc_avg_thermal",
        // Externally asserted thermal throttle.
        "prochot",
        // Running Average Thermal Limit.
        "ratl",
        // Current/voltage protection, not thermal and not a power budget.
        "vr_tdc", "iccmax", "fastvmode",
    ];

    /// <summary>
    /// Folds one asserted reason stem into the neutral flags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PROCHOT counts as thermal: it is an externally asserted over-temperature signal, and the response that
    /// helps is the same one temperature throttling calls for — more airflow.
    /// </para>
    /// <para>
    /// Voltage-regulator current limits (<c>vr_tdc</c>, <c>iccmax</c>, <c>fastvmode</c>) are NOT mapped to
    /// PowerLimit. They are electrical protection rather than a power budget, and more airflow does not
    /// relieve them, so they land in <see cref="ComputeThrottleReasons.Other"/> where the controller will not
    /// escalate on them.
    /// </para>
    /// </remarks>
    public static ComputeThrottleReasons Map(string reasonStem) => reasonStem switch
    {
        "pl1" or "pl2" or "pl4" or "psys_pl1" or "psys_pl2" => ComputeThrottleReasons.PowerLimit,
        "thermal" or "soc_thermal" or "mem_thermal" or "vr_thermal" or "vr_thermalert"
            or "soc_avg_thermal" or "ratl" or "prochot" => ComputeThrottleReasons.ThermalLimit,
        _ => ComputeThrottleReasons.Other,
    };

    /// <summary>
    /// Combines the asserted reason stems into one flag set.
    /// </summary>
    /// <param name="assertedStems">The stems whose attribute read back as 1.</param>
    /// <remarks>
    /// An empty input yields <see cref="ComputeThrottleReasons.None"/> — "asked, and nothing is throttling",
    /// which is a real answer and deliberately NOT the same as the null the caller uses for "could not ask".
    /// </remarks>
    public static ComputeThrottleReasons Combine(IEnumerable<string> assertedStems)
    {
        ArgumentNullException.ThrowIfNull(assertedStems);

        var reasons = ComputeThrottleReasons.None;
        foreach (var stem in assertedStems)
        {
            reasons |= Map(stem);
        }

        return reasons;
    }
}
