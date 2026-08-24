using System.Diagnostics;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Reads Intel GPU busyness from the i915 or xe performance monitoring unit.
/// </summary>
/// <remarks>
/// Intel exposes no busy percentage in sysfs — nothing equivalent to amdgpu's <c>gpu_busy_percent</c> exists.
/// The only trustworthy source is the driver's PMU, read through <c>perf_event_open</c>, which is what
/// <c>intel_gpu_top</c> itself uses.
///
/// The two drivers are genuinely different interfaces, not variations:
/// <list type="bullet">
/// <item>i915 counts engine-busy NANOSECONDS. Busy share is the counter delta over the elapsed enabled time.
/// Crucially, its <c>events/</c> directory already contains one entry per engine that physically exists
/// (<c>rcs0-busy</c>, <c>vcs0-busy</c>, …) with the exact config value, so the engines are DISCOVERED rather
/// than derived from a hand-packed bit layout. That avoids the whole class of encoding mistakes, and it
/// automatically respects fused-off engines and multi-GT parts.</item>
/// <item>xe counts GuC TICKS, as an active/total pair, so busy share is a pure ratio needing no wall clock.
/// Its events are generic, so gt/class/instance are packed into the config using shifts read from the PMU's
/// own <c>format/</c> directory — again discovered, not hardcoded. Engine discovery is by trial open: the
/// PMU rejects an engine that is not present.</item>
/// </list>
///
/// Availability is narrower than the other vendors and that is reported honestly rather than papered over.
/// The xe PMU only exists from kernel 6.15, and engine activity additionally needs a recent GuC firmware, so
/// an Intel machine on an older kernel reports nothing at all. The alternatives — inverting RC6 residency, or
/// summing per-process DRM fdinfo — were considered and rejected: RC6 saturates to "100% busy" the moment the
/// GPU is merely awake, and fdinfo is per-client, double-counts shared file descriptors, misses kernel-side
/// work, and on xe forces the GPU awake to be read. A wrong number in a fan-control app is worse than none.
/// </remarks>
public sealed class LinuxIntelGpuUtilizationReader : IComputeUtilizationReader
{
    private const ushort IntelVendorId = 0x8086;

    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<LinuxIntelGpuUtilizationReader> _logger;
    private readonly DrmSysfs _sysfs;
    private readonly string _eventSourceRoot;
    private readonly Stopwatch _sinceDeviceRefresh = Stopwatch.StartNew();

    private List<IntelGpu> _devices = [];
    private bool _enumerated;
    private bool _loggedSampleFailure;

    // RAPL PP1 ("uncore") — the integrated GPU's own power plane. Resolved once, then sampled as an energy
    // delta like the CPU package figure. See ReadIntegratedGpuPowerWatts.
    private readonly string _powercapRoot;
    private string? _gpuEnergyZone;
    private bool _gpuEnergyZoneResolved;
    private bool _gpuEnergyRangeResolved;
    private double? _gpuEnergyRangeMicrojoules;
    private long? _previousGpuEnergyMicrojoules;
    private long _previousGpuEnergyAt;

    public LinuxIntelGpuUtilizationReader(
        ILogger<LinuxIntelGpuUtilizationReader> logger,
        string sysfsRoot = DrmSysfs.DefaultSysfsRoot,
        string? eventSourceRoot = null)
    {
        _logger = logger;
        _sysfs = new DrmSysfs(sysfsRoot);
        _eventSourceRoot = eventSourceRoot ?? LinuxPerfEvent.EventSourceRoot;
        _powercapRoot = Path.Combine(sysfsRoot, "class", "powercap");
    }

    public bool IsAvailable
    {
        get
        {
            EnsureDevices();
            return _devices.Count > 0;
        }
    }

    public IReadOnlyList<ComputeDeviceUtilization> Sample()
    {
        try
        {
            EnsureDevices();
            if (_devices.Count == 0)
            {
                return [];
            }

            List<ComputeDeviceUtilization> samples = new(_devices.Count);
            foreach (var device in _devices)
            {
                var utilization = device.Sample();
                if (utilization is not null)
                {
                    samples.Add(new ComputeDeviceUtilization
                    {
                        DeviceKey = device.DeviceKey,
                        Kind = ComputeDeviceKind.Gpu,
                        DisplayName = device.DisplayName,
                        UtilizationPercent = utilization.Value,
                        CoreClockMegahertz = ReadMegahertz(device.FrequencyPath),
                        MaxCoreClockMegahertz = ReadMegahertz(device.MaximumFrequencyPath),

                        // No power or temperature, and this is a PLATFORM limitation rather than a choice.
                        // Both of Intel's own routes to it are discrete-only, verified 2026-08-24:
                        //
                        //  1. hwmon — i915_hwmon.c:916 and xe_hwmon.c:1549 both open their register function
                        //     with "/* hwmon is available only for dGfx */ if (!IS_DGFX(...)) return;", so an
                        //     integrated part exposes no power1/energy1/temp1 attribute at all.
                        //  2. Level Zero Sysman — its Linux power module falls back to Intel PMT when hwmon
                        //     is absent, but PMT needs a per-product GUID→offset map, and compute-runtime
                        //     ships one only for the DISCRETE parts (dg2, bmg). The integrated helpers
                        //     (tgllp, adlp, lnl, ptl) inherit the default getGuidToKeyOffsetMap(), which
                        //     returns nullptr — so zesPowerGetEnergyCounter reports the module unsupported.
                        //
                        // So binding Level Zero here would add interop and return "not supported" on every
                        // Intel Framework. IGCL is Windows-only (it loads .dll via LoadLibraryExW and ships
                        // in the Windows driver), so WindowsIgclGpuUtilizationReader has no Linux twin.
                        //
                        // POWER does have a route, and it is not either of the two above: RAPL's PP1 domain
                        // IS the graphics plane on client parts (intel_rapl_common.c documents the matching
                        // perf event as "energy_gpu"), exposed through powercap as the "uncore" zone. That is
                        // read here for the INTEGRATED GPU only — PP1 is a per-package domain, so attaching
                        // it to a discrete card would report the iGPU's draw against the wrong device.
                        //
                        // CAVEAT for any consumer that aggregates: this is a SUBSET of the CPU package figure
                        // the control-telemetry reader publishes, not an addition to it. Summing the two
                        // double counts the iGPU.
                        PowerWatts = device.IsIntegrated ? ReadIntegratedGpuPowerWatts() : null,

                        // Temperature genuinely has no route. An Intel iGPU sits on the CPU die and has no
                        // sensor of its own outside the dGFX-gated hwmon; its die temperature is the CPU
                        // package sensor, which already has its own thermal channel.
                        TemperatureCelsius = null,

                        // Throttle state IS reported on integrated Intel — the one extended signal that is,
                        // and the most useful one for fan control. Null only when the attributes are absent
                        // (pre-Gen11), which says "could not ask" rather than "not throttling".
                        ThrottleReasons = ReadThrottleReasons(device),
                    });
                }
            }

            return samples;
        }
        catch (Exception exception)
        {
            if (!_loggedSampleFailure)
            {
                _loggedSampleFailure = true;
                _logger.LogWarning(exception, "Intel GPU utilization could not be sampled; those devices will report no readings.");
            }

            return [];
        }
    }

    private void EnsureDevices()
    {
        if (_enumerated && _sinceDeviceRefresh.Elapsed < DeviceRefreshInterval)
        {
            return;
        }

        _enumerated = true;
        _sinceDeviceRefresh.Restart();

        try
        {
            // Re-probing tears down the old counters; PMUs come and go with driver bind/unbind.
            foreach (var device in _devices)
            {
                device.Dispose();
            }

            _devices = EnumerateDevices();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Enumerating Intel GPU PMUs failed; Intel GPU utilization will be unavailable.");
            _devices = [];
        }
    }

    private List<IntelGpu> EnumerateDevices()
    {
        List<IntelGpu> devices = [];

        // The PMU directory is named "i915" for an integrated GPU but "i915_<bus_address>" for a discrete one
        // (and xe always carries the address), so both forms are matched by prefix.
        foreach (var pmuDirectory in LinuxPerfEvent.FindPmuDirectories("i915", _eventSourceRoot))
        {
            var device = I915Gpu.TryCreate(pmuDirectory, ResolveName(pmuDirectory, "i915"), _logger);
            if (device is not null)
            {
                ResolveFrequencyPaths(device, pmuDirectory, "i915");
                devices.Add(device);
            }
        }

        foreach (var pmuDirectory in LinuxPerfEvent.FindPmuDirectories("xe_", _eventSourceRoot))
        {
            var device = XeGpu.TryCreate(pmuDirectory, ResolveName(pmuDirectory, "xe"), _logger);
            if (device is not null)
            {
                ResolveFrequencyPaths(device, pmuDirectory, "xe");
                devices.Add(device);
            }
        }

        if (devices.Count > 0)
        {
            _logger.LogInformation(
                "Intel GPU utilization: {Count} device(s) reporting through the {Drivers} PMU.",
                devices.Count,
                string.Join(", ", devices.Select(device => device.DriverName).Distinct()));
        }
        else if (HasIntelGraphics())
        {
            _logger.LogInformation(
                "An Intel GPU is present but exposes no usable performance counters, so its utilization cannot be read. " +
                "The xe driver needs kernel 6.15 or newer (plus recent GuC firmware) for engine activity; i915 needs perf access.");
        }

        return devices;
    }


    /// <summary>
    /// Finds the current-clock attribute for a PMU device, matching the DRM card by PCI address.
    /// </summary>
    /// <remarks>
    /// The two drivers put it in different places: i915 exposes <c>gt_cur_freq_mhz</c> directly on the card,
    /// while xe moved to a per-tile, per-GT tree under the device. Both are read here rather than picking one,
    /// because a Framework 13 can be running either depending on kernel and generation.
    /// </remarks>
    private void ResolveFrequencyPaths(IntelGpu device, string pmuDirectory, string driverName)
    {
        var busAddress = IntelGpuSysfsPaths.ExtractBusAddress(Path.GetFileName(pmuDirectory));

        foreach (var cardName in _sysfs.EnumerateCardNames())
        {
            var devicePath = _sysfs.GetCardDevicePath(cardName);
            var uevent = DrmUevent.Parse(DrmSysfs.ReadAttribute(Path.Combine(devicePath, "uevent")));
            if (uevent.VendorId != IntelVendorId)
            {
                continue;
            }

            // Match the discrete GPU by address; for the integrated one take the first Intel card.
            if (busAddress is not null && !string.Equals(uevent.PciSlotName, busAddress, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cardPath = _sysfs.GetCardPath(cardName);

            device.FrequencyPath = ExistingOrNull(IntelGpuSysfsPaths.GetFrequencyAttributePath(cardPath, devicePath, driverName));
            device.MaximumFrequencyPath = ExistingOrNull(IntelGpuSysfsPaths.GetMaximumFrequencyAttributePath(cardPath, devicePath, driverName));

            // An integrated Intel GPU always sits on PCI bus 0 (00:02.0 conventionally); a discrete card
            // never does. i915 additionally omits the address from the PMU name for the integrated part,
            // which is the same fact arriving by a second route.
            device.IsIntegrated = busAddress is null
                || uevent.PciSlotName?.StartsWith("0000:00:", StringComparison.OrdinalIgnoreCase) == true;

            var (throttleDirectory, throttlePrefix) = IntelGpuSysfsPaths.GetThrottleReasonLocation(cardPath, devicePath, driverName);
            device.ThrottleReasonDirectory = Directory.Exists(throttleDirectory) ? throttleDirectory : null;
            device.ThrottleReasonPrefix = throttlePrefix;
            return;
        }
    }

    private static string? ExistingOrNull(string path) => File.Exists(path) ? path : null;

    /// <summary>
    /// The integrated GPU's power draw, from RAPL's PP1 ("uncore") plane, or null when it is unavailable.
    /// </summary>
    /// <remarks>
    /// An energy counter, so power is the delta over the elapsed window — the same arithmetic (and the same
    /// wrap handling) the CPU package figure uses, reused from <see cref="RaplEnergyMath"/>. Returns null on
    /// the first sample, when there is no window yet.
    /// </remarks>
    private double? ReadIntegratedGpuPowerWatts()
    {
        var zone = ResolveGpuEnergyZone();
        if (zone is null)
        {
            return null;
        }

        if (!TryReadLong(Path.Combine(zone, "energy_uj"), out var microjoules))
        {
            return null;
        }

        var now = Stopwatch.GetTimestamp();
        var previous = _previousGpuEnergyMicrojoules;
        var previousAt = _previousGpuEnergyAt;

        _previousGpuEnergyMicrojoules = microjoules;
        _previousGpuEnergyAt = now;

        if (previous is not { } previousMicrojoules)
        {
            return null;
        }

        return RaplEnergyMath.ComputeWatts(
            previousMicrojoules,
            microjoules,
            Stopwatch.GetElapsedTime(previousAt, now).TotalSeconds,
            ResolveGpuEnergyRangeMicrojoules(zone));
    }

    /// <summary>
    /// Finds the RAPL subzone whose <c>name</c> reads "uncore" — the GPU power plane.
    /// </summary>
    /// <remarks>
    /// Matched on the name file, not the directory index: which index the GPU plane lands on depends on
    /// which domains the part exposes, so keying on <c>intel-rapl:0:1</c> would read the core or dram plane
    /// on some machines. Resolved once; a machine without the domain caches the null and stops looking.
    /// </remarks>
    private string? ResolveGpuEnergyZone()
    {
        if (_gpuEnergyZoneResolved)
        {
            return _gpuEnergyZone;
        }

        _gpuEnergyZoneResolved = true;

        try
        {
            if (!Directory.Exists(_powercapRoot))
            {
                return null;
            }

            foreach (var packageZone in Directory.EnumerateDirectories(_powercapRoot, "intel-rapl:*"))
            {
                foreach (var subzone in Directory.EnumerateDirectories(packageZone, "intel-rapl:*"))
                {
                    if (!RaplEnergyMath.IsSubzoneName(Path.GetFileName(subzone)))
                    {
                        continue;
                    }

                    var name = DrmSysfs.ReadAttribute(Path.Combine(subzone, "name"));
                    if (!string.Equals(name, RaplEnergyMath.GpuDomainName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (File.Exists(Path.Combine(subzone, "energy_uj")))
                    {
                        _gpuEnergyZone = subzone;
                        _logger.LogInformation(
                            "Intel GPU power: reading the integrated GPU's RAPL PP1 plane at {Zone}.",
                            subzone);
                        return _gpuEnergyZone;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "Scanning powercap for the GPU energy plane failed; Intel GPU power will be unavailable.");
        }

        return _gpuEnergyZone;
    }

    /// <summary>The counter's wrap point, needed to interpret a negative delta. Fixed, so resolved once.</summary>
    private double? ResolveGpuEnergyRangeMicrojoules(string zone)
    {
        if (_gpuEnergyRangeResolved)
        {
            return _gpuEnergyRangeMicrojoules;
        }

        _gpuEnergyRangeResolved = true;

        if (TryReadLong(Path.Combine(zone, "max_energy_range_uj"), out var range) && range > 0)
        {
            _gpuEnergyRangeMicrojoules = range;
        }

        return _gpuEnergyRangeMicrojoules;
    }

    private static bool TryReadLong(string path, out long value)
    {
        var raw = DrmSysfs.ReadInt64Attribute(path);
        value = raw ?? 0L;
        return raw is not null;
    }

    /// <summary>
    /// Reads the per-reason throttle attributes, or null when this device exposes none.
    /// </summary>
    /// <remarks>
    /// Each attribute is a boolean; the set of files present varies by platform, so a stem with no file is
    /// simply not asserted. Null is returned only when the whole directory is missing, which is the honest
    /// "could not ask" — distinct from None, on which the fan controller is allowed to relax.
    /// </remarks>
    private static ComputeThrottleReasons? ReadThrottleReasons(IntelGpu device)
    {
        if (device.ThrottleReasonDirectory is not { } directory)
        {
            return null;
        }

        List<string> asserted = [];
        var sawAnyAttribute = false;

        foreach (var stem in IntelGpuThrottleReasons.ReasonStems)
        {
            var path = Path.Combine(directory, device.ThrottleReasonPrefix + stem);
            var value = DrmSysfs.ReadInt64Attribute(path);
            if (value is null)
            {
                continue;
            }

            sawAnyAttribute = true;
            if (value.Value != 0)
            {
                asserted.Add(stem);
            }
        }

        // A directory that exists but yields no readable attribute is not an answer of "not throttling".
        return sawAnyAttribute ? IntelGpuThrottleReasons.Combine(asserted) : null;
    }

    /// <summary>Reads a clock attribute, which both drivers report in megahertz already.</summary>
    private static double? ReadMegahertz(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var megahertz = DrmSysfs.ReadInt64Attribute(path);
        return megahertz is null or <= 0 ? null : megahertz.Value;
    }

    /// <summary>True when the machine has Intel graphics at all, used only to explain an empty result.</summary>
    private bool HasIntelGraphics()
    {
        foreach (var cardName in _sysfs.EnumerateCardNames())
        {
            var uevent = DrmUevent.Parse(DrmSysfs.ReadAttribute(Path.Combine(_sysfs.GetCardDevicePath(cardName), "uevent")));
            if (uevent.VendorId == IntelVendorId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Names the device from its PMU directory, resolving the PCI address embedded in a discrete GPU's name
    /// back to a marketing name through pci.ids.
    /// </summary>
    private string ResolveName(string pmuDirectory, string driverName)
    {
        // "i915_0000_03_00.0" -> "0000:03:00.0"; the bare "i915" is the integrated GPU.
        var directoryName = Path.GetFileName(pmuDirectory);
        var underscore = directoryName.IndexOf('_');
        var busAddress = underscore > 0
            ? directoryName[(underscore + 1)..].Replace('_', ':')
            : null;

        foreach (var cardName in _sysfs.EnumerateCardNames())
        {
            var uevent = DrmUevent.Parse(DrmSysfs.ReadAttribute(Path.Combine(_sysfs.GetCardDevicePath(cardName), "uevent")));
            if (uevent.VendorId != IntelVendorId)
            {
                continue;
            }

            // Match the discrete GPU by address; for the integrated one take the first Intel card.
            if (busAddress is not null && !string.Equals(uevent.PciSlotName, busAddress, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (uevent.DeviceId is { } deviceId)
            {
                var names = PciIdDatabase.Lookup([new PciDeviceId(IntelVendorId, deviceId)]);
                if (names.TryGetValue(new PciDeviceId(IntelVendorId, deviceId), out var resolved) && resolved.DeviceName is not null)
                {
                    return resolved.DeviceName;
                }
            }

            break;
        }

        return busAddress is null ? "Intel GPU" : $"Intel GPU ({busAddress})";
    }

    public void Dispose()
    {
        foreach (var device in _devices)
        {
            device.Dispose();
        }

        _devices = [];
    }

    /// <summary>One Intel GPU whose PMU counters are open and being differenced.</summary>
    private abstract class IntelGpu(string deviceKey, string displayName) : IDisposable
    {
        public string DeviceKey { get; } = deviceKey;

        public string DisplayName { get; } = displayName;

        /// <summary>
        /// Path to the current-clock attribute, or null when this device does not expose one. Assigned after
        /// construction because devices are discovered from the PMU tree while the clock lives in the DRM
        /// tree, and the two are matched by PCI address rather than being adjacent.
        /// </summary>
        public string? FrequencyPath { get; set; }

        /// <summary>Path to the permitted-maximum clock, which FrequencyPath is measured against.</summary>
        public string? MaximumFrequencyPath { get; set; }

        /// <summary>
        /// True for the integrated GPU. RAPL's PP1 plane is per-package and describes the iGPU, so only the
        /// integrated device may claim it.
        /// </summary>
        public bool IsIntegrated { get; set; }

        /// <summary>Directory holding the per-reason throttle attributes, or null when the part exposes none.</summary>
        public string? ThrottleReasonDirectory { get; set; }

        /// <summary>Filename prefix those attributes carry — driver-specific, so it travels with the directory.</summary>
        public string ThrottleReasonPrefix { get; set; } = IntelGpuThrottleReasons.I915AttributePrefix;

        public abstract string DriverName { get; }

        /// <summary>Busy share since the previous call, or null when the counters cannot be read.</summary>
        public abstract double? Sample();

        public abstract void Dispose();
    }

    /// <summary>
    /// i915: per-engine nanosecond counters, discovered from the PMU's own <c>events/</c> listing.
    /// </summary>
    private sealed class I915Gpu : IntelGpu
    {
        private readonly List<EngineCounter> _engines;

        private I915Gpu(string deviceKey, string displayName, List<EngineCounter> engines)
            : base(deviceKey, displayName) => _engines = engines;

        public override string DriverName => "i915";

        public static I915Gpu? TryCreate(string pmuDirectory, string displayName, ILogger logger)
        {
            var type = LinuxPerfEvent.ReadPmuType(pmuDirectory);
            if (type == 0)
            {
                return null;
            }

            var cpus = LinuxPerfEvent.ReadCandidateCpus(pmuDirectory);
            var eventsDirectory = Path.Combine(pmuDirectory, "events");

            List<EngineCounter> engines = [];
            try
            {
                if (!Directory.Exists(eventsDirectory))
                {
                    return null;
                }

                foreach (var file in Directory.EnumerateFiles(eventsDirectory))
                {
                    var name = Path.GetFileName(file);

                    // Engine busy counters only: "rcs0-busy", "vcs0-busy". The sibling ".unit" files and the
                    // frequency/rc6/interrupt counters are not utilization.
                    if (!name.EndsWith("-busy", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var config = LinuxPerfEvent.ParseEventConfig(DrmSysfs.ReadAttribute(file));
                    if (config is null)
                    {
                        continue;
                    }

                    var perfEvent = LinuxPerfEvent.TryOpen(type, config.Value, cpus);
                    if (perfEvent is null)
                    {
                        continue;
                    }

                    // The engine class is the name up to the trailing instance digits ("rcs0-busy" -> "rcs").
                    engines.Add(new EngineCounter(GetEngineClass(name), perfEvent));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                foreach (var engine in engines)
                {
                    engine.Dispose();
                }

                return null;
            }

            if (engines.Count == 0)
            {
                // Almost always a permissions problem: a system-wide perf event needs CAP_PERFMON, and a
                // container's default seccomp policy blocks the syscall outright even for root.
                logger.LogInformation(
                    "The i915 PMU at {PmuDirectory} exposed no readable engine counters (perf_event_open denied or no engines); Intel GPU utilization will not be reported.",
                    pmuDirectory);
                return null;
            }

            return new I915Gpu(Path.GetFileName(pmuDirectory), displayName, engines);
        }

        private static string GetEngineClass(string eventName)
        {
            var span = eventName.AsSpan(0, eventName.Length - "-busy".Length);
            var end = span.Length;
            while (end > 0 && char.IsAsciiDigit(span[end - 1]))
            {
                end--;
            }

            return span[..end].ToString();
        }

        public override double? Sample()
        {
            // Per class: the mean over that class's engine instances, matching intel_gpu_top's aggregation.
            // Across classes: the maximum, never the sum — engines run concurrently, so summing exceeds 100%
            // and describes nothing. This mirrors the "max across engine types" rule the Windows reader uses.
            Dictionary<string, (double Sum, int Count)> byClass = [];
            var anyRead = false;

            foreach (var engine in _engines)
            {
                var busy = engine.SampleBusyPercent();
                if (busy is null)
                {
                    continue;
                }

                anyRead = true;
                var existing = byClass.GetValueOrDefault(engine.EngineClass);
                byClass[engine.EngineClass] = (existing.Sum + busy.Value, existing.Count + 1);
            }

            if (!anyRead || byClass.Count == 0)
            {
                return null;
            }

            var maximum = 0d;
            foreach (var (sum, count) in byClass.Values)
            {
                maximum = Math.Max(maximum, sum / count);
            }

            return Math.Clamp(maximum, 0d, 100d);
        }

        public override void Dispose()
        {
            foreach (var engine in _engines)
            {
                engine.Dispose();
            }

            _engines.Clear();
        }

        private sealed class EngineCounter(string engineClass, LinuxPerfEvent perfEvent) : IDisposable
        {
            private ulong _previousBusyNanoseconds;
            private ulong _previousTimeEnabled;
            private bool _primed;

            public string EngineClass { get; } = engineClass;

            /// <summary>Busy nanoseconds accrued over the enabled time since the last sample.</summary>
            public double? SampleBusyPercent()
            {
                if (!perfEvent.TryRead(out var busy, out var timeEnabled))
                {
                    return null;
                }

                if (!_primed)
                {
                    // First read establishes the baseline; a percentage needs two points.
                    _primed = true;
                    _previousBusyNanoseconds = busy;
                    _previousTimeEnabled = timeEnabled;
                    return null;
                }

                var busyDelta = busy - _previousBusyNanoseconds;
                var timeDelta = timeEnabled - _previousTimeEnabled;

                _previousBusyNanoseconds = busy;
                _previousTimeEnabled = timeEnabled;

                if (timeDelta == 0)
                {
                    return null;
                }

                // Both sides are nanoseconds, so this is a pure ratio. Clamp: read skew can overshoot slightly.
                return Math.Clamp(busyDelta * 100d / timeDelta, 0d, 100d);
            }

            public void Dispose() => perfEvent.Dispose();
        }
    }

    /// <summary>
    /// xe: paired active/total GuC tick counters per engine, with the config fields packed using shifts read
    /// from the PMU's own <c>format/</c> directory.
    /// </summary>
    private sealed class XeGpu : IntelGpu
    {
        private const int MaxGt = 2;
        private const int MaxEngineClass = 4;
        private const int MaxEngineInstance = 8;

        private readonly List<XeEngineCounter> _engines;

        private XeGpu(string deviceKey, string displayName, List<XeEngineCounter> engines)
            : base(deviceKey, displayName) => _engines = engines;

        public override string DriverName => "xe";

        public static XeGpu? TryCreate(string pmuDirectory, string displayName, ILogger logger)
        {
            var type = LinuxPerfEvent.ReadPmuType(pmuDirectory);
            if (type == 0)
            {
                return null;
            }

            var eventsDirectory = Path.Combine(pmuDirectory, "events");
            var activeConfig = LinuxPerfEvent.ParseEventConfig(DrmSysfs.ReadAttribute(Path.Combine(eventsDirectory, "engine-active-ticks")));
            var totalConfig = LinuxPerfEvent.ParseEventConfig(DrmSysfs.ReadAttribute(Path.Combine(eventsDirectory, "engine-total-ticks")));

            if (activeConfig is null || totalConfig is null)
            {
                // Kernel 6.15 introduced the xe PMU, and engine activity additionally needs GuC 1.14.1+.
                // Older combinations expose the PMU without these events; that is a genuine "cannot report".
                return null;
            }

            var formatDirectory = Path.Combine(pmuDirectory, "format");
            var gtShift = LinuxPerfEvent.ParseFormatShift(DrmSysfs.ReadAttribute(Path.Combine(formatDirectory, "gt")));
            var classShift = LinuxPerfEvent.ParseFormatShift(DrmSysfs.ReadAttribute(Path.Combine(formatDirectory, "engine_class")));
            var instanceShift = LinuxPerfEvent.ParseFormatShift(DrmSysfs.ReadAttribute(Path.Combine(formatDirectory, "engine_instance")));

            if (gtShift is null || classShift is null || instanceShift is null)
            {
                logger.LogDebug("The xe PMU at {PmuDirectory} did not describe its config layout; skipping.", pmuDirectory);
                return null;
            }

            var cpus = LinuxPerfEvent.ReadCandidateCpus(pmuDirectory);
            List<XeEngineCounter> engines = [];

            // The PMU itself is the authority on which engines exist: an absent one is rejected at open.
            for (var gt = 0; gt < MaxGt; gt++)
            {
                for (var engineClass = 0; engineClass <= MaxEngineClass; engineClass++)
                {
                    for (var instance = 0; instance < MaxEngineInstance; instance++)
                    {
                        var selector = ((ulong)gt << gtShift.Value)
                            | ((ulong)engineClass << classShift.Value)
                            | ((ulong)instance << instanceShift.Value);

                        var active = LinuxPerfEvent.TryOpen(type, activeConfig.Value | selector, cpus);
                        if (active is null)
                        {
                            continue;
                        }

                        var total = LinuxPerfEvent.TryOpen(type, totalConfig.Value | selector, cpus);
                        if (total is null)
                        {
                            active.Dispose();
                            continue;
                        }

                        engines.Add(new XeEngineCounter(engineClass, active, total));
                    }
                }
            }

            if (engines.Count == 0)
            {
                logger.LogInformation(
                    "The xe PMU at {PmuDirectory} exposed no readable engine counters; Intel GPU utilization will not be reported.",
                    pmuDirectory);
                return null;
            }

            return new XeGpu(Path.GetFileName(pmuDirectory), displayName, engines);
        }

        public override double? Sample()
        {
            Dictionary<int, (double Sum, int Count)> byClass = [];
            var anyRead = false;

            foreach (var engine in _engines)
            {
                var busy = engine.SampleBusyPercent();
                if (busy is null)
                {
                    continue;
                }

                anyRead = true;
                var existing = byClass.GetValueOrDefault(engine.EngineClass);
                byClass[engine.EngineClass] = (existing.Sum + busy.Value, existing.Count + 1);
            }

            if (!anyRead || byClass.Count == 0)
            {
                return null;
            }

            var maximum = 0d;
            foreach (var (sum, count) in byClass.Values)
            {
                maximum = Math.Max(maximum, sum / count);
            }

            return Math.Clamp(maximum, 0d, 100d);
        }

        public override void Dispose()
        {
            foreach (var engine in _engines)
            {
                engine.Dispose();
            }

            _engines.Clear();
        }

        private sealed class XeEngineCounter(int engineClass, LinuxPerfEvent active, LinuxPerfEvent total) : IDisposable
        {
            private ulong _previousActive;
            private ulong _previousTotal;
            private bool _primed;

            public int EngineClass { get; } = engineClass;

            /// <summary>
            /// Active ticks over total ticks. Both are in the GPU clock domain, so no wall clock is involved
            /// and the result is immune to sampling jitter.
            /// </summary>
            public double? SampleBusyPercent()
            {
                if (!active.TryRead(out var activeTicks, out _) || !total.TryRead(out var totalTicks, out _))
                {
                    return null;
                }

                if (!_primed)
                {
                    _primed = true;
                    _previousActive = activeTicks;
                    _previousTotal = totalTicks;
                    return null;
                }

                var activeDelta = activeTicks - _previousActive;
                var totalDelta = totalTicks - _previousTotal;

                _previousActive = activeTicks;
                _previousTotal = totalTicks;

                // A fully idle GT advances neither counter; that is "no data", not "zero busy".
                return totalDelta == 0 ? null : Math.Clamp(activeDelta * 100d / totalDelta, 0d, 100d);
            }

            public void Dispose()
            {
                active.Dispose();
                total.Dispose();
            }
        }
    }
}
