using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// Decides ONCE which combination of power readings a machine's feed-forward runs on, then holds that choice.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a policy and not a per-tick choice.</b> Picking the best reading available at each tick sounds
/// obviously right and is quietly wrong. A discrete GPU reports power under load and drops out in a low-power
/// state, so a per-tick choice flips composition several times a minute — and every flip changes what P
/// MEANS. The estimator would then either refuse half its samples or, worse, fold both meanings into one fit.
/// </para>
/// <para>
/// The refusals are also not random: a GPU only reports while it is busy, so accepting only those samples
/// means learning exclusively from loaded states. A fit biased toward one end of the operating range is worse
/// than a fit that took longer to gather.
/// </para>
/// <para>
/// <b>How the choice is made.</b> Availability is counted over a short window, and a reading only joins the
/// composition if it was there for most of it. That single rule handles the flapping GPU without special
/// cases: a card that reports only under load never reaches the threshold, so it is excluded — and on Windows
/// the fallback already contains its power anyway, because the charger sees everything.
/// </para>
/// <para>
/// Feed-forward is NOT withheld while the window runs. The controller uses the best reading available from
/// the first tick; only LEARNING waits, because only learning depends on P meaning the same thing every time.
/// </para>
/// </remarks>
public sealed class ThermalLoadPolicy
{
    /// <summary>
    /// Samples observed before the composition is fixed.
    /// </summary>
    /// <remarks>
    /// At the controller's one-second evaluation this is about a minute — long enough to see a GPU enter and
    /// leave a low-power state, short enough that learning is not held up meaningfully against the tens of
    /// minutes the fit needs anyway.
    /// </remarks>
    public const int CapabilityWindowSamples = 60;

    /// <summary>
    /// Fraction of the window a reading must be present for to join the composition.
    /// </summary>
    /// <remarks>
    /// Below 1.0 because a single dropped read — a driver reloading, a transient EC failure — should not
    /// permanently exclude an otherwise reliable source. Well above 0.5 so an intermittent one cannot sneak
    /// in on a coin flip.
    /// </remarks>
    public const double RequiredAvailabilityFraction = 0.8d;

    private int _samplesSeen;
    private int _cpuSeen;
    private int _gpuSeen;
    private int _systemSeen;

    /// <summary>Creates a policy that has not yet decided.</summary>
    public ThermalLoadPolicy()
    {
    }

    /// <summary>Creates a policy already fixed to a composition, resumed from persisted state.</summary>
    /// <param name="source">The composition a previous run settled on.</param>
    /// <remarks>
    /// Resuming matters: re-running the capability window on every service restart could land on a different
    /// composition than the stored fit was built from, silently invalidating days of learning.
    /// </remarks>
    public ThermalLoadPolicy(ThermalLoadSource source)
    {
        if (source != ThermalLoadSource.None)
        {
            Source = source;
            IsSettled = true;
        }
    }

    /// <summary>The composition in force, or <see cref="ThermalLoadSource.None"/> while undecided.</summary>
    public ThermalLoadSource Source { get; private set; }

    /// <summary>True once the composition is fixed and samples may be learned from.</summary>
    public bool IsSettled { get; private set; }

    /// <summary>
    /// Returns the load figure for a sample, and whether it may be learned from.
    /// </summary>
    /// <param name="sample">This tick's readings.</param>
    /// <returns>
    /// The watts to feed forward, the composition they represent, and whether the composition is settled.
    /// Watts is null when the settled composition cannot be formed from this sample — a refusal, deliberately
    /// not a substitution, because quietly swapping in a different reading is how a fit gets corrupted.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="sample"/> is null.</exception>
    public (double? Watts, ThermalLoadSource Source, bool IsSettled) Resolve(ControlTelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var cpu = Usable(sample.CpuPackagePowerWatts);
        var gpu = Usable(sample.GpuPowerWatts);
        var system = Usable(sample.SystemPowerWatts);

        if (!IsSettled)
        {
            Observe(cpu, gpu, system);
        }

        // Before the window closes, run on the best reading THIS sample can form. Feed-forward is useful from
        // the first tick; it is only learning that needs a stable meaning.
        var source = IsSettled ? Source : Rank(cpu is not null, gpu is not null, system is not null);

        return (Compose(source, cpu, gpu, system), source, IsSettled);
    }

    private void Observe(double? cpu, double? gpu, double? system)
    {
        _samplesSeen++;

        if (cpu is not null)
        {
            _cpuSeen++;
        }

        if (gpu is not null)
        {
            _gpuSeen++;
        }

        if (system is not null)
        {
            _systemSeen++;
        }

        if (_samplesSeen < CapabilityWindowSamples)
        {
            return;
        }

        var threshold = _samplesSeen * RequiredAvailabilityFraction;
        Source = Rank(_cpuSeen >= threshold, _gpuSeen >= threshold, _systemSeen >= threshold);
        IsSettled = true;
    }

    /// <summary>Picks the best composition formable from the available readings, ranked by coverage.</summary>
    private static ThermalLoadSource Rank(bool hasCpu, bool hasGpu, bool hasSystem)
    {
        if (hasCpu && hasGpu)
        {
            return ThermalLoadSource.CpuAndGpu;
        }

        if (hasCpu)
        {
            return ThermalLoadSource.Cpu;
        }

        // System outranks a GPU-only reading deliberately: it is coarse but complete, where GPU-only is
        // precise about the wrong fraction and reads a CPU-bound machine as idle.
        if (hasSystem)
        {
            return ThermalLoadSource.System;
        }

        return hasGpu ? ThermalLoadSource.Gpu : ThermalLoadSource.None;
    }

    private static double? Compose(ThermalLoadSource source, double? cpu, double? gpu, double? system)
        => source switch
        {
            // Both parts required. A missing half is a refusal, not a smaller number — treating an absent GPU
            // as zero would teach the model that this machine cools an imaginary load.
            ThermalLoadSource.CpuAndGpu => cpu is double c && gpu is double g ? c + g : null,
            ThermalLoadSource.Cpu => cpu,
            ThermalLoadSource.System => system,
            ThermalLoadSource.Gpu => gpu,
            _ => null,
        };

    private static double? Usable(double? watts)
        => watts is double value && double.IsFinite(value) && value >= 0d ? value : null;
}
