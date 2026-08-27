namespace SubZeroFramework.Models;

/// <summary>
/// Which combination of power readings the adaptive controller's feed-forward load figure is built from.
/// </summary>
/// <remarks>
/// <para>
/// This is not diagnostics — it is a correctness requirement. The online estimator fits
/// <c>T ≈ a + b·P − K·duty</c>, and <c>b</c> is the coupling between whatever P it was fed and this fan's
/// zone temperature. That coupling is completely different for "CPU package watts" than for "total system
/// watts at the wall", so a fit built from one is meaningless applied to the other.
/// </para>
/// <para>
/// The values are ordered by COVERAGE, not by directness, and that ordering is the whole point. A coarse
/// measure of all the heat beats a precise measure of some of it: during a compile the GPU sits near idle
/// while the CPU pulls 60 W, so a GPU-only figure would leave feed-forward contributing nothing at exactly
/// the moment anticipation is worth most.
/// </para>
/// </remarks>
public enum ThermalLoadSource
{
    /// <summary>No usable reading. Feed-forward is inert and the loop runs on feedback alone.</summary>
    None = 0,

    /// <summary>
    /// CPU package power plus GPU power — essentially every watt entering the chassis, and each moving the
    /// instant its workload starts. The best available anywhere, and reachable on Linux, where RAPL exposes
    /// package power.
    /// </summary>
    CpuAndGpu = 1,

    /// <summary>
    /// CPU package power alone. Misses a discrete GPU, but covers the dominant source on most machines.
    /// </summary>
    Cpu = 2,

    /// <summary>
    /// System draw from the charger, less battery charging. Coarser — it carries the display, the SSD and
    /// everything else — but it captures ALL the heat, which is why it outranks any partial component
    /// reading. The Windows answer, where no package power reaches user mode without a kernel driver.
    /// </summary>
    System = 3,

    /// <summary>
    /// GPU power alone. The last resort, deliberately ranked below <see cref="System"/>: it is a precise
    /// measure of the wrong fraction, and a machine under CPU load would read as idle.
    /// </summary>
    Gpu = 4,
}
