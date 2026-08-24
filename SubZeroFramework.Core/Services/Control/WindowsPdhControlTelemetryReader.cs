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
    private const string PerformanceCounterPath = @"\Processor Information(_Total)\% Processor Performance";

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

    private SafePDH_HQUERY _query = SafePDH_HQUERY.Null;
    private PDH_HCOUNTER _utilityCounter;
    private PDH_HCOUNTER _performanceCounter;
    private SafeHGlobalHandle _itemBuffer = SafeHGlobalHandle.Null;
    private uint _itemBufferSize;

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

            var (aggregate, perCore) = ReadUtilization();
            var performance = ReadScalarCounter(_performanceCounter, PerformanceCounterPath);

            return new ControlTelemetrySample
            {
                CpuUtilizationFraction = aggregate,
                PerCoreUtilizationFraction = perCore,

                // Deliberately NOT clamped. Values above 1 mean turbo, and erasing them would collapse the
                // difference between "at rated speed" and "boosting hard" — measured 121-136% on the dev
                // machine under load, so this is the normal case, not an edge one.
                CpuPerformanceRatio = performance is { } performancePercent ? performancePercent / 100d : null,

                // No package power on Windows without a kernel driver; the controller substitutes adapter power.
                CpuPackagePowerWatts = null,
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

            _itemBuffer.Dispose();
            _itemBuffer = SafeHGlobalHandle.Null;
            _itemBufferSize = 0;
        }
    }

    private unsafe (double? Aggregate, ImmutableArray<double> PerCore) ReadUtilization()
    {
        // Vanara ships no managed wrapper for PdhGetFormattedCounterArray, so the two-call sizing dance stays:
        // ask with the buffer we already hold, and only grow it when PDH says it is too small.
        var bufferSize = _itemBufferSize;
        var status = PdhGetFormattedCounterArray(_utilityCounter, CounterFormat, ref bufferSize, out var itemCount, _itemBuffer.DangerousGetHandle());
        if (status == PdhMoreData)
        {
            EnsureItemBuffer(bufferSize);
            bufferSize = _itemBufferSize;
            status = PdhGetFormattedCounterArray(_utilityCounter, CounterFormat, ref bufferSize, out itemCount, _itemBuffer.DangerousGetHandle());
        }

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

        if (itemCount == 0 || _itemBuffer.IsInvalid)
        {
            return (null, []);
        }

        double? aggregate = null;
        _perCoreScratch.Clear();

        var items = new ReadOnlySpan<PdhFormattedCounterValueItem>((void*)_itemBuffer.DangerousGetHandle(), checked((int)itemCount));
        foreach (var item in items)
        {
            if (item.Name.IsNull || (uint)item.Value.CStatus is not (PdhCStatusValidData or PdhCStatusNewData))
            {
                continue;
            }

            // PDH writes the instance names into the tail of the same buffer, so this reads them in place
            // rather than allocating a string per processor per tick.
            var instanceName = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item.Name);

            // Utility can exceed 100% when boosting above nominal. These are busy FRACTIONS by definition, so
            // they are clamped; the speed information is not lost, it is what CpuPerformanceRatio carries.
            var fraction = Math.Clamp(item.Value.doubleValue / 100d, 0d, 1d);

            if (ProcessorInstanceName.IsMachineTotal(instanceName))
            {
                aggregate = fraction;
                continue;
            }

            // Rejects the per-group "{group},_Total" rollups too, which would otherwise each appear as an
            // extra processor carrying the average of its group.
            if (ProcessorInstanceName.TryParse(instanceName, out var group, out var processor))
            {
                _perCoreScratch.Add((ProcessorInstanceName.ToOrdinal(group, processor), fraction));
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

    private void EnsureItemBuffer(uint requiredBytes)
    {
        if (requiredBytes <= _itemBufferSize && !_itemBuffer.IsInvalid)
        {
            return;
        }

        _itemBuffer.Dispose();
        _itemBufferSize = 0;

        // A little slack, because the instance count only changes when logical processors are parked or
        // hot-added — far more stable than the GPU engine set, so no large margin is warranted.
        var size = requiredBytes + 64;
        _itemBuffer = new SafeHGlobalHandle(size);
        _itemBufferSize = size;
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

            if (!TryAddCounter(query, UtilityCounterPath, out var utilityCounter)
                || !TryAddCounter(query, PerformanceCounterPath, out var performanceCounter))
            {
                query.Dispose();
                return false;
            }

            _utilityCounter = utilityCounter;
            _performanceCounter = performanceCounter;
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

    private bool TryAddCounter(SafePDH_HQUERY query, string counterPath, out PDH_HCOUNTER counter)
    {
        // The English variant, so the path still resolves on a localized Windows where the counter set is
        // named in the user's language.
        var status = PdhAddEnglishCounter(query, counterPath, IntPtr.Zero, out var added);
        if (status != PdhSuccess)
        {
            counter = default;
            _logger.LogInformation(
                "CPU control telemetry unavailable: the '{CounterPath}' counter returned 0x{Status:X8}.",
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
