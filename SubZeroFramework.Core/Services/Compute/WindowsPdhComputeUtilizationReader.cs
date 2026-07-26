// Compiled only into the windows TFM of Core (SubZeroFramework.Core.csproj conditions the Vanara references
// the same way), so a Linux publish carries neither this type nor its interop dependencies.
#if WINDOWS10_0_26100_0_OR_GREATER
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vanara.InteropServices;
using static Vanara.PInvoke.Pdh;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Reads GPU and NPU busy time on Windows from the in-box <c>GPU Engine</c> PDH counter set.
/// </summary>
/// <remarks>
/// <para>
/// One query is opened once and kept open for the life of this object, because reopening it is where the cost
/// hides: a full wildcard collect against a persistent query measures 1.4-1.8 ms on the dev machine's 723
/// engine instances, while <c>Get-Counter</c> — which reopens every time — measures ~3 s for the same data.
/// </para>
/// <para>
/// The NPU needs no second source. Windows enumerates an MCDM compute-only accelerator as its own adapter
/// inside this same counter set, so one query covers AMD, NVIDIA, Intel and the NPU alike. A LocalSystem
/// service in session 0 sees exactly the instances an interactive user does, NPU included.
/// </para>
/// <para>
/// Microsoft publishes neither this counter set's NPU behaviour nor the instance-name layout — this
/// replicates Task Manager's internal logic — so every failure here degrades to "no devices" rather than
/// throwing out of a telemetry tick.
/// </para>
/// </remarks>
// Core is cross-platform and also runs on Linux, and every Vanara assembly is marked windows-only, so the
// platform is declared here rather than defended call by call: the service only ever constructs this type
// inside an OperatingSystem.IsWindows() branch, which is what the compiler now checks.
[SupportedOSPlatform("windows")]
public sealed partial class WindowsPdhComputeUtilizationReader : IComputeUtilizationReader
{
    private const string RunningTimeCounterPath = @"\GPU Engine(*)\Running Time";

    private const uint PdhSuccess = 0x00000000;
    private const uint PdhMoreData = 0x800007D2;

    // Per-item status: anything else means PDH could not cook that instance this round.
    private const uint PdhCStatusValidData = 0x00000000;
    private const uint PdhCStatusNewData = 0x00000001;

    // "Running Time" is counter type NumberOfItems64 (PERF_COUNTER_LARGE_RAWCOUNT) — verified on the dev
    // machine, where the cooked value equals the raw value exactly — so it is a plain 64-bit accumulator that
    // PDH does not rate-convert. LARGE hands back those ticks losslessly; PDH_FMT_DOUBLE would push them
    // through a double and start shedding low bits once the accumulator passes 2^53.
    private const PDH_FMT CounterFormat = PDH_FMT.PDH_FMT_LARGE;

    // Running Time accumulates in 100-nanosecond units. VERIFIED rather than assumed: summing the deltas per
    // adapter+engine over the elapsed wall clock and dividing by 10,000,000 reproduced Windows' own
    // "Utilization Percentage" for the same instances to the digit (9.11% and 0.21% on a 1.02 s window).
    private const double RunningTimeTicksPerSecond = 10_000_000d;

    // The device set is near-static, and the resolver costs hundreds of milliseconds, so it must never run on
    // the sampling cadence. Hotplug is handled separately by the unknown-LUID trigger below.
    private static readonly TimeSpan IdentityRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<WindowsPdhComputeUtilizationReader> _logger;
    private readonly IComputeDeviceIdentityResolver _identityResolver;
    private readonly Lock _syncLock = new();

    private readonly Dictionary<AdapterKey, ComputeDeviceIdentity> _identities = [];

    // Adapters the resolver has already failed to name. Without this, an adapter that simply has no OS
    // identity would look like a hotplug on every single tick and re-run the expensive enumeration forever.
    private readonly HashSet<AdapterKey> _unnamedAdapters = [];

    private SafePDH_HQUERY _query = SafePDH_HQUERY.Null;
    private PDH_HCOUNTER _counter;
    private SafeHGlobalHandle _itemBuffer = SafeHGlobalHandle.Null;
    private uint _itemBufferSize;

    private Dictionary<AdapterKey, Dictionary<string, long>>? _previousSums;
    private long _previousTimestamp;

    private long _identitiesResolvedAt;
    private bool _hasResolvedIdentities;

    private bool _loggedCollectFailure;
    private bool _loggedArrayFailure;
    private bool _loggedResolverFailure;
    private bool _loggedUnexpectedFailure;
    private bool _loggedSampleCost;
    private bool _disposed;

    public WindowsPdhComputeUtilizationReader(
        ILogger<WindowsPdhComputeUtilizationReader> logger,
        IComputeDeviceIdentityResolver identityResolver)
    {
        _logger = logger;
        _identityResolver = identityResolver;
        IsAvailable = OperatingSystem.IsWindows() && TryOpenQuery();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public IReadOnlyList<ComputeDeviceUtilization> Sample()
    {
        if (!IsAvailable)
        {
            return [];
        }

        lock (_syncLock)
        {
            if (_disposed)
            {
                return [];
            }

            try
            {
                return SampleCore();
            }
            catch (Exception ex)
            {
                // The counter layout is undocumented and the interop is native on both counts, so treat any
                // surprise as "this machine cannot report utilization" instead of failing the telemetry tick.
                if (!_loggedUnexpectedFailure)
                {
                    _loggedUnexpectedFailure = true;
                    _logger.LogWarning(ex, "GPU/NPU utilization sampling failed; no compute devices will be reported.");
                }

                return [];
            }
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

            // Closing the query releases its counters too, so the counter handle needs no separate close —
            // which is exactly why TryOpenQuery neutralized Vanara's counter safe handle instead of keeping it.
            _query.Dispose();
            _query = SafePDH_HQUERY.Null;
            _counter = default;

            _itemBuffer.Dispose();
            _itemBuffer = SafeHGlobalHandle.Null;
            _itemBufferSize = 0;
        }
    }

    private IReadOnlyList<ComputeDeviceUtilization> SampleCore()
    {
        var startedAt = Stopwatch.GetTimestamp();

        var status = PdhCollectQueryData(_query);
        if (status != PdhSuccess)
        {
            if (!_loggedCollectFailure)
            {
                _loggedCollectFailure = true;
                _logger.LogInformation("GPU/NPU utilization: PDH collect returned 0x{Status:X8}; no compute devices will be reported.", (uint)status);
            }

            return [];
        }

        var sums = ReadRunningTimeSums();
        var collectedAt = Stopwatch.GetTimestamp();
        if (sums is null)
        {
            return [];
        }

        if (!_loggedSampleCost)
        {
            _loggedSampleCost = true;
            _logger.LogInformation(
                "GPU/NPU utilization: read {AdapterCount} adapter(s) from the GPU Engine counter set in {ElapsedMs:F2} ms.",
                sums.Count,
                Stopwatch.GetElapsedTime(startedAt, collectedAt).TotalMilliseconds);
        }

        RefreshIdentitiesIfNeeded(sums, collectedAt);

        var previous = _previousSums;
        var previousTimestamp = _previousTimestamp;
        _previousSums = sums;
        _previousTimestamp = collectedAt;

        // Busy time is a delta over a window, and the first sample has no window. Reporting zeros here would
        // claim "everything is idle" for one tick, which is a lie the graphs would keep forever; reporting
        // the accumulator itself would be a spike of whatever the machine did since boot. Report nothing, and
        // let the telemetry layer treat the devices as briefly unavailable — the next tick has a baseline.
        if (previous is null)
        {
            return [];
        }

        // Monotonic elapsed, so a clock adjustment mid-window cannot manufacture a 900% reading.
        return BuildUtilization(sums, previous, Stopwatch.GetElapsedTime(previousTimestamp, collectedAt));
    }

    private List<ComputeDeviceUtilization> BuildUtilization(
        Dictionary<AdapterKey, Dictionary<string, long>> current,
        Dictionary<AdapterKey, Dictionary<string, long>> previous,
        TimeSpan elapsed)
    {
        var elapsedSeconds = elapsed.TotalSeconds;
        if (elapsedSeconds <= 0d)
        {
            return [];
        }

        var results = new List<ComputeDeviceUtilization>(current.Count);

        // Ordered so a given set of adapters is always reported in the same order, whatever order the counter
        // instances happened to arrive in.
        foreach (var adapter in current.Keys.OrderBy(key => key.Luid).ThenBy(key => key.PhysicalIndex))
        {
            var engines = current[adapter];
            if (!previous.TryGetValue(adapter, out var previousEngines))
            {
                // Newly appeared adapter: no baseline yet, same reasoning as the first sample.
                continue;
            }

            var hasBaseline = false;
            var busiest = 0d;

            foreach (var (engineType, ticks) in engines)
            {
                if (!previousEngines.TryGetValue(engineType, out var previousTicks))
                {
                    continue;
                }

                hasBaseline = true;

                // Instances are per process and transient, so a process exiting removes its accumulated ticks
                // from the sum and the delta goes negative. That under-reports the tick it happens on, which
                // is the honest outcome — the alternative is inventing a number.
                var delta = ticks - previousTicks;
                if (delta <= 0)
                {
                    continue;
                }

                // Engines run concurrently, so the device's load is the busiest engine, not the total; summing
                // them would routinely exceed 100%. This is what Task Manager reports.
                busiest = Math.Max(busiest, delta / RunningTimeTicksPerSecond / elapsedSeconds * 100d);
            }

            if (!hasBaseline)
            {
                continue;
            }

            var adapterUtilization = Math.Clamp(busiest, 0d, 100d);
            if (CreateUtilization(adapter, adapterUtilization) is { } utilization)
            {
                LogAdapterSampled(utilization.DisplayName, adapterUtilization, engines.Count);
                results.Add(utilization);
            }
            else
            {
                // Dropped for want of a resolved identity — the phantom-adapter filter described on
                // CreateUtilization. Worth tracing: it is also what a genuinely hotplugged GPU looks like
                // for the seconds before the resolver catches up.
                LogAdapterWithoutIdentity(adapter.Luid, adapter.PhysicalIndex, adapterUtilization);
            }
        }

        return results;
    }

    /// <summary>
    /// Builds the reading for an adapter, or null when the OS has no device behind it.
    /// </summary>
    /// <remarks>
    /// Counter adapters are NOT all real hardware. This machine carries a fourth adapter with ~250 instances
    /// that has no PnP device at all — a WDDM software renderer (Basic Render Driver / WARP). Publishing it
    /// under a synthesized name would put a GPU in the user's list that does not exist in their computer,
    /// which is the same phantom-device mistake the expansion bay taught us in 0.1.1. Only devices the
    /// identity resolver actually enumerated are reported; a genuinely hotplugged GPU appears as soon as the
    /// resolver refreshes (which an unknown LUID triggers), a delay of seconds rather than a permanent loss.
    /// </remarks>
    private ComputeDeviceUtilization? CreateUtilization(AdapterKey adapter, double utilizationPercent)
    {
        if (!_identities.TryGetValue(adapter, out var identity))
        {
            return null;
        }

        return new ComputeDeviceUtilization
        {
            DeviceKey = identity.DeviceKey,
            Kind = identity.Kind,
            DisplayName = identity.DisplayName,
            UtilizationPercent = utilizationPercent,
        };
    }

    private void RefreshIdentitiesIfNeeded(Dictionary<AdapterKey, Dictionary<string, long>> observed, long timestamp)
    {
        var due = !_hasResolvedIdentities
            || Stopwatch.GetElapsedTime(_identitiesResolvedAt, timestamp) >= IdentityRefreshInterval;

        if (!due)
        {
            foreach (var adapter in observed.Keys)
            {
                // An adapter we have neither named nor already failed to name means the device set changed
                // under us — an eGPU plugged in, a driver reload — which the slow cadence would hide for
                // minutes.
                if (!_identities.ContainsKey(adapter) && !_unnamedAdapters.Contains(adapter))
                {
                    due = true;
                    break;
                }
            }
        }

        if (!due)
        {
            return;
        }

        // Stamped before the call so a resolver that keeps failing is retried on the slow cadence, not per tick.
        _hasResolvedIdentities = true;
        _identitiesResolvedAt = timestamp;
        _identities.Clear();
        _unnamedAdapters.Clear();

        IReadOnlyList<ComputeDeviceIdentity> devices;
        try
        {
            devices = _identityResolver.Enumerate();
        }
        catch (Exception ex)
        {
            if (!_loggedResolverFailure)
            {
                _loggedResolverFailure = true;
                _logger.LogWarning(ex, "Compute device identity enumeration failed; devices will be reported under generic names.");
            }

            devices = [];
        }

        foreach (var device in devices)
        {
            if (device.AdapterLuid is not { } luid || device.PhysicalAdapterIndex is not { } physicalIndex)
            {
                continue;
            }

            _identities[new AdapterKey(luid, physicalIndex)] = device;
        }

        foreach (var adapter in observed.Keys)
        {
            if (!_identities.ContainsKey(adapter))
            {
                _unnamedAdapters.Add(adapter);
            }
        }
    }

    private unsafe Dictionary<AdapterKey, Dictionary<string, long>>? ReadRunningTimeSums()
    {
        // Vanara ships no managed wrapper for PdhGetFormattedCounterArray, so the two-call sizing dance stays:
        // ask with the buffer we already hold, and only grow it when PDH says it is too small.
        var bufferSize = _itemBufferSize;
        var status = PdhGetFormattedCounterArray(_counter, CounterFormat, ref bufferSize, out var itemCount, _itemBuffer.DangerousGetHandle());
        if (status == PdhMoreData)
        {
            EnsureItemBuffer(bufferSize);
            bufferSize = _itemBufferSize;
            status = PdhGetFormattedCounterArray(_counter, CounterFormat, ref bufferSize, out itemCount, _itemBuffer.DangerousGetHandle());
        }

        if (status != PdhSuccess)
        {
            if (!_loggedArrayFailure)
            {
                _loggedArrayFailure = true;
                _logger.LogInformation("GPU/NPU utilization: PDH counter array returned 0x{Status:X8}; no compute devices will be reported.", (uint)status);
            }

            return null;
        }

        var sums = new Dictionary<AdapterKey, Dictionary<string, long>>();
        if (itemCount == 0 || _itemBuffer.IsInvalid)
        {
            return sums;
        }

        var items = new ReadOnlySpan<PdhFormattedCounterValueItem>((void*)_itemBuffer.DangerousGetHandle(), checked((int)itemCount));
        foreach (var item in items)
        {
            if (item.Name.IsNull || (uint)item.Value.CStatus is not (PdhCStatusValidData or PdhCStatusNewData))
            {
                continue;
            }

            // PDH writes the instance names into the tail of the same buffer, so this reads them in place —
            // 700-odd instance names per second is not worth allocating.
            var instanceName = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item.Name);
            if (!GpuEngineInstanceName.TryParse(instanceName, out var instance) || item.Value.largeValue < 0)
            {
                continue;
            }

            var adapter = new AdapterKey(instance.Luid, instance.PhysicalIndex);
            if (!sums.TryGetValue(adapter, out var engines))
            {
                // Case-insensitive because the engine label's casing is the driver's choice and has been seen
                // to differ between Windows builds for the same hardware.
                engines = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                sums[adapter] = engines;
            }

            engines.TryGetValue(instance.EngineType, out var total);
            engines[instance.EngineType] = total + item.Value.largeValue;
        }

        return sums;
    }

    private void EnsureItemBuffer(uint requiredBytes)
    {
        if (requiredBytes <= _itemBufferSize && !_itemBuffer.IsInvalid)
        {
            return;
        }

        _itemBuffer.Dispose();
        _itemBuffer = SafeHGlobalHandle.Null;
        _itemBufferSize = 0;

        // A quarter of slack, because the instance count tracks how many processes are touching the GPU and
        // that churns constantly; without it a busy machine reallocates on most ticks.
        var size = requiredBytes + (requiredBytes / 4) + 1;
        _itemBuffer = new SafeHGlobalHandle(size);
        _itemBufferSize = size;
    }

    private bool TryOpenQuery()
    {
        try
        {
            var status = PdhOpenQuery(null, IntPtr.Zero, out var query);
            if (status != PdhSuccess)
            {
                _logger.LogInformation("GPU/NPU utilization unavailable: PdhOpenQuery returned 0x{Status:X8}.", (uint)status);
                return false;
            }

            // The English variant, so the path still resolves on a localized Windows where the counter set is
            // named in the user's language.
            status = PdhAddEnglishCounter(query, RunningTimeCounterPath, IntPtr.Zero, out var counter);
            if (status != PdhSuccess)
            {
                query.Dispose();
                _logger.LogInformation(
                    "GPU/NPU utilization unavailable: the '{CounterPath}' counter returned 0x{Status:X8}.",
                    RunningTimeCounterPath,
                    (uint)status);
                return false;
            }

            // Closing the query releases its counters too, so the counter handle needs no separate close.
            // Vanara's SafePDH_HCOUNTER would still call PdhRemoveCounter on it — against a query that may
            // already be gone, in whatever order finalization ran — so the raw handle is kept and its safe
            // handle neutralized, leaving the query as the single owner of both.
            _counter = counter;
            counter.SetHandleAsInvalid();

            _query = query;
            return true;
        }
        catch (Exception ex)
        {
            // A Windows install without pdh.dll, or with an export we cannot bind, is a machine that reports
            // no compute devices — not a machine that fails to start the service.
            _logger.LogWarning(ex, "GPU/NPU utilization unavailable: the PDH counter query could not be opened.");
            return false;
        }
    }

    /// <summary>Identifies one adapter within the current Windows session.</summary>
    private readonly record struct AdapterKey(long Luid, int PhysicalIndex);

    /// <summary>
    /// <c>PDH_FMT_COUNTERVALUE_ITEM_W</c>, declared here rather than taken from Vanara because Vanara's
    /// <c>PDH_FMT_COUNTERVALUE_ITEM</c> types its name field as a marshalled <see langword="string"/>. That
    /// makes the struct non-blittable, which rules out reading the array in place out of PDH's own buffer and
    /// would allocate a string for every one of the 700-odd instances on every sample. Only the name differs:
    /// the value is Vanara's <c>PDH_FMT_COUNTERVALUE</c>, whose 8-byte union members put it at offset 8
    /// exactly as the C struct's alignment does.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValueItem
    {
        public StrPtrUni Name;
        public PDH_FMT_COUNTERVALUE Value;
    }

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "{DisplayName} is {UtilizationPercent:F0}% busy (busiest of {EngineCount} engine type(s)).")]
    private partial void LogAdapterSampled(string displayName, double utilizationPercent, int engineCount);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "PDH adapter LUID 0x{Luid:X} index {PhysicalIndex} measured {UtilizationPercent:F0}% but has no enumerated PnP device; not reported.")]
    private partial void LogAdapterWithoutIdentity(long luid, int physicalIndex, double utilizationPercent);
}
#endif
