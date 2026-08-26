// Compiled only into the windows TFM of Core (SubZeroFramework.Core.csproj conditions the Vanara references
// the same way), so a Linux publish carries neither this type nor its interop dependencies.
#if WINDOWS10_0_26100_0_OR_GREATER
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

using Vanara.InteropServices;

using static Vanara.PInvoke.Pdh;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// Reads CPU utilisation and the clock-performance ratio on Windows from the in-box
/// <c>Processor Information</c> PDH counter set.
/// </summary>
/// <remarks>
/// <para>
/// This replaces <c>Hardware.Info</c>'s <c>RefreshCPUList(true, 500, true)</c> on the control path. That call
/// sampled, slept a blocking half second, sampled again and differenced — a measured ~600 ms out of a 1 s
/// budget, on top of WMI, for the controller's only anticipatory input. PDH maintains the equivalent counters
/// itself, so the same information costs one collect. Measured on the dev machine: ~1.1 ms per tick.
/// </para>
/// <para>
/// Two counters. <c>% Processor Utility</c> is read across the WILDCARD instance, which yields the machine
/// rollup and every logical processor from a single array — the aggregate the controller runs on and the
/// per-core figures the UI draws, for one collect rather than two queries. It is busy time adjusted for the
/// speed the processor was actually running at (Task Manager's CPU figure), which makes it a better heat proxy
/// than plain busy time. <c>% Processor Performance</c> is read as a scalar against <c>_Total</c>: it is
/// current clock over base clock, which IS the throttle signal, so no separate frequency read is needed.
/// </para>
/// <para>
/// Both are rate counters that PDH differentiates internally, so the FIRST sample after the query opens has no
/// window to report over and yields nothing. That is expected of every PDH rate counter, not a failure.
/// </para>
/// </remarks>
// Core is cross-platform and also runs on Linux, and every Vanara assembly is marked windows-only, so the
// platform is declared here rather than defended call by call.
[SupportedOSPlatform("windows")]
public sealed class WindowsPdhControlTelemetryReader : IControlTelemetryReader
{
    private const string UtilityCounterPath = @"\Processor Information(*)\% Processor Utility";

    /// <summary>
    /// Each processor's clock as a percentage of its nominal clock, over 100 when boosting.
    /// </summary>
    /// <remarks>
    /// Read as a wildcard ARRAY rather than the machine total alone, and it costs no extra counter to do so —
    /// the array carries <c>_Total</c> as one of its instances, so a single read serves both the ratio
    /// published on the sample and the per-processor divisor the utilisation pass needs.
    /// </remarks>
    private const string PerformanceCounterPath = @"\Processor Information(*)\% Processor Performance";

    /// <summary>
    /// The clock the processor is actually running at, in megahertz.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note this is <c>Actual Frequency</c> and NOT the similarly named <c>Processor Frequency</c> in the same
    /// counter set. The latter reports the BASE clock and is a constant — measured 2000 MHz on a Ryzen AI 9
    /// HX 370 whether idle or fully loaded — so a reader that picked it would publish a plausible megahertz
    /// figure that never moves. Actual Frequency measured 3563 MHz idle against 4066 MHz under load.
    /// </para>
    /// <para>
    /// OPTIONAL, like the energy meter below: not every machine populates it, and a machine that does not must
    /// still get utilisation and the performance ratio.
    /// </para>
    /// </remarks>
    private const string ActualFrequencyCounterPath = @"\Processor Information(_Total)\Actual Frequency";

    /// <summary>
    /// Package power, from the in-box Energy Meter counter set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows surfaces the platform's energy meters — including the processor's own RAPL domains — through
    /// PDH, so the package figure needs no kernel driver and no MSR access. Measured on a Ryzen AI 9 HX 370:
    /// 16 W at idle, 48 W under an eight-thread load, tracking within a sample.
    /// </para>
    /// <para>
    /// OPTIONAL, unlike the two above. A machine whose firmware exposes no energy meter simply has no
    /// instances here, and that must not cost it CPU utilisation as well — so a failure to add this counter
    /// leaves the rest of the query working.
    /// </para>
    /// </remarks>
    private const string EnergyMeterCounterPath = @"\Energy Meter(*)\Power";

    /// <summary>
    /// Which energy-meter instance is the processor package, best first.
    /// </summary>
    /// <remarks>
    /// The instance names come from the platform, so they are matched by preference rather than assumed. The
    /// RAPL package domain is the same quantity Linux reads from sysfs, which is what keeps the two platforms
    /// reporting the same thing. "socket"/"apu" is the vendor's own package rollup where RAPL is absent, and
    /// "cpu" is cores-only — a real undercount on an APU, hence last.
    /// </remarks>
    private static readonly string[] PackagePowerInstancePreference =
    [
        "rapl_package0_pkg",
        "socket power",
        "apu power",
        "cpu power",
    ];

    /// <summary>The Energy Meter counter set reports in milliwatts.</summary>
    private const double MilliwattsPerWatt = 1000d;

    private const uint PdhSuccess = 0x00000000;
    private const uint PdhMoreData = 0x800007D2;

    // What a rate counter returns before it has two samples to differentiate. Expected once per query, so it
    // is filtered out of the failure logging rather than reported as a problem.
    private const uint PdhInvalidData = 0x800007D5;

    // Per-item status: anything else means PDH could not cook that counter this round. Both of these appear on
    // the first collect of a rate counter, which is why they read as "no data yet" rather than as an error.
    private const uint PdhCStatusValidData = 0x00000000;
    private const uint PdhCStatusNewData = 0x00000001;

    private const PDH_FMT CounterFormat = PDH_FMT.PDH_FMT_DOUBLE;

    private readonly ILogger<WindowsPdhControlTelemetryReader> _logger;
    private readonly Lock _syncLock = new();

    // Reused across ticks and sorted in place, so a steady state costs no per-tick allocation beyond the
    // immutable array handed to the caller.
    private readonly List<(long Ordinal, double Fraction)> _perCoreScratch = [];

    /// <summary>
    /// Each processor's speed ratio this tick, keyed by the ordinal the utilisation pass uses.
    /// </summary>
    /// <remarks>
    /// Per processor rather than one machine-wide figure because a hybrid part does not run its cores at one
    /// speed. Measured on a Ryzen AI 9 HX 370 under load: the Zen5 cores reported 204% of nominal while the
    /// Zen5c cores reported 146% at the same instant. Dividing every core by the machine total would
    /// understate busy time on the fast cores and overstate it on the dense ones.
    /// </remarks>
    private readonly Dictionary<long, double> _performanceRatioByOrdinal = [];

    private SafePDH_HQUERY _query = SafePDH_HQUERY.Null;
    private PDH_HCOUNTER _utilityCounter;
    private PDH_HCOUNTER _performanceCounter;

    /// <summary>Default when the platform does not populate Actual Frequency, which leaves the clock unreported.</summary>
    private PDH_HCOUNTER _actualFrequencyCounter;
    private bool _hasActualFrequency;

    /// <summary>Default when the platform exposes no energy meter, which leaves package power unreported.</summary>
    private PDH_HCOUNTER _energyMeterCounter;
    private bool _hasEnergyMeter;
    // One buffer per counter, never shared. The utilisation pass walks a span over its own buffer while the
    // others are being filled, and a shared buffer could resize out from under that span.
    private readonly CounterArray _utilityArray = new();
    private readonly CounterArray _performanceArray = new();
    private readonly CounterArray _powerArray = new();

    private bool _attemptedOpen;
    private bool _loggedCollectFailure;
    private bool _loggedArrayFailure;
    private bool _loggedCounterFailure;
    private bool _disposed;

    public WindowsPdhControlTelemetryReader(ILogger<WindowsPdhControlTelemetryReader> logger)
        => _logger = logger;

    public bool IsAvailable
    {
        get
        {
            lock (_syncLock)
            {
                return !_disposed && EnsureQuery();
            }
        }
    }

    public ControlTelemetrySample Sample()
    {
        lock (_syncLock)
        {
            if (_disposed || !EnsureQuery())
            {
                return ControlTelemetrySample.Unavailable;
            }

            var status = PdhCollectQueryData(_query);
            if (status != PdhSuccess)
            {
                if (!_loggedCollectFailure)
                {
                    _loggedCollectFailure = true;
                    _logger.LogInformation(
                        "CPU control telemetry: PDH collect returned 0x{Status:X8}; the adaptive controller will run without CPU signals.",
                        (uint)status);
                }

                return ControlTelemetrySample.Unavailable;
            }

            // Ratios first: the utilisation pass divides by them to cancel the clock out of its readings.
            var performanceRatio = ReadPerformanceRatios();
            var (aggregate, perCore) = ReadUtilization(performanceRatio);

            return new ControlTelemetrySample
            {
                CpuUtilizationFraction = aggregate,
                PerCoreUtilizationFraction = perCore,

                // Deliberately NOT clamped. Values above 1 mean turbo, and erasing them would collapse the
                // difference between "at rated speed" and "boosting hard" — measured 1.21-2.07 on the dev
                // machine under load, so this is the normal case, not an edge one.
                CpuPerformanceRatio = performanceRatio,

                // The live clock, so the CPU package card stops showing a figure from the thirty-second
                // inventory tier next to power read on this one.
                CpuClockMegahertz = ReadActualClockMegahertz(),

                // From the platform's own energy meters via PDH — the same RAPL package domain Linux reads
                // from sysfs, and no kernel driver for either. Null only where the firmware exposes no meter,
                // in which case the controller falls back to system power as it always did.
                CpuPackagePowerWatts = ReadPackagePowerWatts(),
            };
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _query.Dispose();
            _query = SafePDH_HQUERY.Null;
            _utilityCounter = default;
            _performanceCounter = default;
            _actualFrequencyCounter = default;
            _hasActualFrequency = false;
            _energyMeterCounter = default;
            _hasEnergyMeter = false;

            _utilityArray.Dispose();
            _performanceArray.Dispose();
            _powerArray.Dispose();
            _performanceRatioByOrdinal.Clear();
        }
    }

    /// <param name="totalPerformanceRatio">
    /// The machine-wide speed ratio, used for the aggregate and as the fallback divisor for any processor the
    /// performance array did not report.
    /// </param>
    private unsafe (double? Aggregate, ImmutableArray<double> PerCore) ReadUtilization(double? totalPerformanceRatio)
    {
        var items = _utilityArray.Read(_utilityCounter, out var status);

        if (status != PdhSuccess)
        {
            // The first collect of a rate counter lands here with PDH_INVALID_DATA — there is no window yet.
            if (status != PdhInvalidData && !_loggedArrayFailure)
            {
                _loggedArrayFailure = true;
                _logger.LogInformation(
                    "CPU control telemetry: the '{CounterPath}' array returned 0x{Status:X8}; CPU utilisation will not be reported.",
                    UtilityCounterPath,
                    (uint)status);
            }

            return (null, []);
        }

        if (items.IsEmpty)
        {
            return (null, []);
        }

        double? aggregate = null;
        _perCoreScratch.Clear();

        foreach (var item in items)
        {
            if (item.Name.IsNull || (uint)item.Value.CStatus is not (PdhCStatusValidData or PdhCStatusNewData))
            {
                continue;
            }

            // PDH writes the instance names into the tail of the same buffer, so this reads them in place
            // rather than allocating a string per processor per tick.
            var instanceName = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item.Name);
            var utility = item.Value.doubleValue / 100d;

            if (ProcessorInstanceName.IsMachineTotal(instanceName))
            {
                aggregate = ProcessorUtilityMath.ToBusyFraction(utility, totalPerformanceRatio);
                continue;
            }

            // Rejects the per-group "{group},_Total" rollups too, which would otherwise each appear as an
            // extra processor carrying the average of its group.
            if (ProcessorInstanceName.TryParse(instanceName, out var group, out var processor))
            {
                var ordinal = ProcessorInstanceName.ToOrdinal(group, processor);
                var ratio = _performanceRatioByOrdinal.TryGetValue(ordinal, out var perCoreRatio)
                    ? perCoreRatio
                    : totalPerformanceRatio;

                _perCoreScratch.Add((ordinal, ProcessorUtilityMath.ToBusyFraction(utility, ratio)));
            }
        }

        // PDH's enumeration order is not guaranteed, and the UI draws these as a fixed row of bars, so a
        // stable group-then-processor order is what keeps a given bar meaning the same core between ticks.
        _perCoreScratch.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));

        var perCore = ImmutableArray.CreateBuilder<double>(_perCoreScratch.Count);
        foreach (var (_, fraction) in _perCoreScratch)
        {
            perCore.Add(fraction);
        }

        return (aggregate, perCore.MoveToImmutable());
    }

    /// <summary>
    /// Reads every processor's speed ratio, returning the machine-wide one and caching the rest by ordinal.
    /// </summary>
    /// <remarks>
    /// Runs before the utilisation pass because that pass consumes what this caches. Both read from the same
    /// collect, so the two arrays describe the same instant rather than straddling a clock change.
    /// </remarks>
    private unsafe double? ReadPerformanceRatios()
    {
        _performanceRatioByOrdinal.Clear();

        var items = _performanceArray.Read(_performanceCounter, out var status);

        if (status != PdhSuccess || items.IsEmpty)
        {
            // Expected on the first collect of a rate counter. Not logged: the utilisation pass reports the
            // same condition against the same query, and two lines per cause is noise.
            return null;
        }

        double? total = null;

        foreach (var item in items)
        {
            if (item.Name.IsNull || (uint)item.Value.CStatus is not (PdhCStatusValidData or PdhCStatusNewData))
            {
                continue;
            }

            var instanceName = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item.Name);
            var ratio = item.Value.doubleValue / 100d;

            if (ProcessorInstanceName.IsMachineTotal(instanceName))
            {
                total = ratio;
                continue;
            }

            if (ProcessorInstanceName.TryParse(instanceName, out var group, out var processor))
            {
                _performanceRatioByOrdinal[ProcessorInstanceName.ToOrdinal(group, processor)] = ratio;
            }
        }

        return total;
    }

    /// <summary>
    /// The clock the processor is actually running at, or null when this machine does not report one.
    /// </summary>
    /// <remarks>
    /// Zero is treated as no reading rather than as a stopped processor. PDH answers a rate counter's first
    /// collect with no interval to average over, and a 0 MHz clock published into the UI would read as a
    /// hung machine for the one tick it survived.
    /// </remarks>
    private double? ReadActualClockMegahertz()
    {
        if (!_hasActualFrequency)
        {
            return null;
        }

        return ReadScalarCounter(_actualFrequencyCounter, ActualFrequencyCounterPath) is { } megahertz and > 0d
            ? megahertz
            : null;
    }

    /// <summary>
    /// Package power in watts, picked from the energy meter's instances by preference.
    /// </summary>
    /// <remarks>
    /// Scans the whole array once and keeps the best-ranked instance rather than stopping at the first match,
    /// because PDH does not enumerate in any guaranteed order — taking the first would report cores-only power
    /// on one boot and the package on the next.
    /// </remarks>
    private unsafe double? ReadPackagePowerWatts()
    {
        if (!_hasEnergyMeter)
        {
            return null;
        }

        var items = _powerArray.Read(_energyMeterCounter, out var status);

        if (status != PdhSuccess || items.IsEmpty)
        {
            // PDH_INVALID_DATA on the first collect is expected of a rate counter, and an absent meter is a
            // fact about the machine — neither is worth a log line every tick.
            return null;
        }

        var bestRank = int.MaxValue;
        double? best = null;

        foreach (var item in items)
        {
            if (item.Name.IsNull || (uint)item.Value.CStatus is not (PdhCStatusValidData or PdhCStatusNewData))
            {
                continue;
            }

            var instanceName = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item.Name);

            for (var rank = 0; rank < PackagePowerInstancePreference.Length && rank < bestRank; rank++)
            {
                if (!instanceName.Equals(PackagePowerInstancePreference[rank], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Zero is what an unpopulated domain reports — an NPU with no work, say — and treating it as a
                // reading would hand the controller a hard zero for package power.
                if (item.Value.doubleValue > 0d)
                {
                    bestRank = rank;
                    best = item.Value.doubleValue / MilliwattsPerWatt;
                }

                break;
            }
        }

        return best;
    }

    private double? ReadScalarCounter(PDH_HCOUNTER counter, string counterPath)
    {
        var status = PdhGetFormattedCounterValue(counter, CounterFormat, out _, out var value);
        if (status != PdhSuccess)
        {
            // Again, the first collect of a rate counter has no window to report over.
            return null;
        }

        if ((uint)value.CStatus is not (PdhCStatusValidData or PdhCStatusNewData))
        {
            if (!_loggedCounterFailure)
            {
                _loggedCounterFailure = true;
                _logger.LogInformation(
                    "CPU control telemetry: '{CounterPath}' reported status 0x{Status:X8} and is omitted from the sample.",
                    counterPath,
                    (uint)value.CStatus);
            }

            return null;
        }

        return value.doubleValue;
    }

    /// <summary>
    /// One PDH counter array's native buffer, and the two-call sizing dance that fills it.
    /// </summary>
    /// <remarks>
    /// Vanara ships no managed wrapper for <c>PdhGetFormattedCounterArray</c>, so the caller must ask once to
    /// learn the size and again to read. That sequence was written out per counter until there were three of
    /// them; it lives here instead so a new counter costs a field rather than another copy.
    /// </remarks>
    private sealed class CounterArray : IDisposable
    {
        private SafeHGlobalHandle _buffer = SafeHGlobalHandle.Null;
        private uint _sizeBytes;

        /// <summary>
        /// Reads the array, growing the buffer if PDH says it is too small.
        /// </summary>
        /// <param name="counter">The counter to read.</param>
        /// <param name="status">PDH's status, so the caller can tell "no data yet" from a real failure.</param>
        /// <returns>The items, or empty when nothing could be read.</returns>
        public unsafe ReadOnlySpan<PdhFormattedCounterValueItem> Read(PDH_HCOUNTER counter, out uint status)
        {
            var required = _sizeBytes;
            var result = PdhGetFormattedCounterArray(counter, CounterFormat, ref required, out var itemCount, _buffer.DangerousGetHandle());

            if (result == PdhMoreData)
            {
                Grow(required);
                required = _sizeBytes;
                result = PdhGetFormattedCounterArray(counter, CounterFormat, ref required, out itemCount, _buffer.DangerousGetHandle());
            }

            status = (uint)result;

            return status != PdhSuccess || itemCount == 0 || _buffer.IsInvalid
                ? []
                : new ReadOnlySpan<PdhFormattedCounterValueItem>((void*)_buffer.DangerousGetHandle(), checked((int)itemCount));
        }

        public void Dispose()
        {
            _buffer.Dispose();
            _buffer = SafeHGlobalHandle.Null;
            _sizeBytes = 0;
        }

        private void Grow(uint requiredBytes)
        {
            if (requiredBytes <= _sizeBytes && !_buffer.IsInvalid)
            {
                return;
            }

            _buffer.Dispose();
            _sizeBytes = 0;

            // A little slack, because the instance count only changes when logical processors are parked or
            // hot-added — far more stable than the GPU engine set, so no large margin is warranted.
            var size = requiredBytes + 64;
            _buffer = new SafeHGlobalHandle(size);
            _sizeBytes = size;
        }
    }

    private bool EnsureQuery()
    {
        if (!_query.IsNull)
        {
            return true;
        }

        // One attempt for the life of the object. A machine whose PDH cannot serve this counter set will not
        // start serving it later, and retrying every tick would put the failure cost back on the fast path.
        if (_attemptedOpen)
        {
            return false;
        }

        _attemptedOpen = true;
        return TryOpenQuery();
    }

    private bool TryOpenQuery()
    {
        SafePDH_HQUERY? query = null;

        try
        {
            var status = PdhOpenQuery(null, IntPtr.Zero, out query);
            if (status != PdhSuccess)
            {
                _logger.LogInformation("CPU control telemetry unavailable: PdhOpenQuery returned 0x{Status:X8}.", (uint)status);
                return false;
            }

            if (!TryAddCounter(query, UtilityCounterPath, required: true, out var utilityCounter)
                || !TryAddCounter(query, PerformanceCounterPath, required: true, out var performanceCounter))
            {
                query.Dispose();
                return false;
            }

            _utilityCounter = utilityCounter;
            _performanceCounter = performanceCounter;

            // Optional: a machine with no energy meter still reports utilisation, and losing that as well
            // would trade a nice-to-have for the controller's primary input.
            _hasEnergyMeter = TryAddCounter(query, EnergyMeterCounterPath, required: false, out var energyMeterCounter);
            _energyMeterCounter = _hasEnergyMeter ? energyMeterCounter : default;

            // Optional on the same terms, and for a display figure rather than a control one.
            _hasActualFrequency = TryAddCounter(query, ActualFrequencyCounterPath, required: false, out var actualFrequencyCounter);
            _actualFrequencyCounter = _hasActualFrequency ? actualFrequencyCounter : default;

            _query = query;
            return true;
        }
        catch (Exception exception)
        {
            // A Windows install without pdh.dll, or with an export we cannot bind, is a machine that reports no
            // CPU control telemetry — not a machine that fails to start the service.
            query?.Dispose();
            _logger.LogWarning(exception, "CPU control telemetry unavailable: the PDH counter query could not be opened.");
            return false;
        }
    }

    /// <param name="required">
    /// False for a counter whose absence costs only its own field. It changes the LOG WORDING and nothing
    /// else: most of these counters are optional, and reporting each missing one as "control telemetry
    /// unavailable" would describe a machine that is merely missing an energy meter as having no telemetry
    /// at all.
    /// </param>
    private bool TryAddCounter(SafePDH_HQUERY query, string counterPath, bool required, out PDH_HCOUNTER counter)
    {
        // The English variant, so the path still resolves on a localized Windows where the counter set is
        // named in the user's language.
        var status = PdhAddEnglishCounter(query, counterPath, IntPtr.Zero, out var added);
        if (status != PdhSuccess)
        {
            counter = default;
            _logger.LogInformation(
                required
                    ? "CPU control telemetry unavailable: the '{CounterPath}' counter returned 0x{Status:X8}."
                    : "CPU control telemetry: the optional '{CounterPath}' counter returned 0x{Status:X8}; that field will be unreported and the rest continue.",
                counterPath,
                (uint)status);
            return false;
        }

        // Closing the query releases its counters too, so the counter handle needs no separate close. Vanara's
        // SafePDH_HCOUNTER would still call PdhRemoveCounter on it — against a query that may already be gone,
        // in whatever order finalization ran — so the raw handle is kept and its safe handle neutralized,
        // leaving the query as the single owner of both.
        counter = added;
        added.SetHandleAsInvalid();
        return true;
    }

    /// <summary>
    /// <c>PDH_FMT_COUNTERVALUE_ITEM_W</c>. Declared here rather than taken from Vanara for the same reason
    /// <c>WindowsPdhComputeUtilizationReader</c> declares its own: Vanara's
    /// <c>PDH_FMT_COUNTERVALUE_ITEM</c> types the name field as a marshalled <see langword="string"/>, which
    /// makes the struct non-blittable and rules out reading the array in place out of PDH's own buffer. Only
    /// the name differs; the value is Vanara's <c>PDH_FMT_COUNTERVALUE</c>, whose 8-byte union members put it
    /// at offset 8 exactly as the C struct's alignment does.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValueItem
    {
        public StrPtrUni Name;
        public PDH_FMT_COUNTERVALUE Value;
    }
}
#endif
