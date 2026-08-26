using System.Diagnostics;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Reads NVIDIA GPU power, temperature, clocks and throttle reasons on Windows through NVML.
/// </summary>
/// <remarks>
/// <para>
/// Windows already reports GPU UTILISATION for every vendor through the PDH <c>GPU Engine</c> counter set,
/// and that counter set has no power, temperature or clock at all. This reader exists to supply what PDH
/// cannot, for the one vendor that exposes it — which on a Framework 16 with the NVIDIA graphics module is
/// the primary GPU, not an exotic case.
/// </para>
/// <para>
/// <b>Sampling happens on a background thread, never on the caller's.</b> Measured on a Framework 16 with an
/// RTX 5070: most NVML calls cost 0.02 ms, but roughly every third one stalls 480-590 ms and returns
/// <c>NVML_ERROR_UNKNOWN</c> for utilisation, with board power jumping 19 W to 29 W on exactly those calls —
/// the laptop dGPU changing power state. <c>nvmlInit_v2</c> alone costs 385-870 ms. Calling any of that
/// inline from a polling tier would stall the whole tier for half a second at a time, so
/// <see cref="Sample"/> only ever returns the most recent completed reading.
/// </para>
/// <para>
/// Devices are keyed by the OS device instance path rather than by PCI address, so a device published here
/// lines up with the same device published by the PDH reader and the composite can merge the two into one
/// entry instead of showing the GPU twice. The join runs on the canonical PCI address: NVML reports
/// <c>0000:c2:00.0</c>, and the identity resolver derives the same string from the numeric PnP properties.
/// </para>
/// </remarks>
public sealed partial class WindowsNvmlGpuUtilizationReader : IComputeUtilizationReader
{
    /// <summary>How long a published reading stays usable before it is treated as no reading at all.</summary>
    /// <remarks>
    /// Generous next to any tier interval: this is a "the sampler has stopped or wedged" guard, not a
    /// freshness requirement. Without it a dead sampler would leave its last values on screen forever.
    ///
    /// It MUST outlast <see cref="NvmlSamplingBackoff.SleepingInterval"/>. While the reader is backing off a
    /// sleeping GPU it deliberately samples only once a minute, and a shorter lifetime here would expire that
    /// reading between samples — the GPU block would blink out and back rather than sitting still.
    /// </remarks>
    private static readonly TimeSpan ReadingLifetime = TimeSpan.FromSeconds(180);

    /// <summary>The device set is near-static, and the resolver costs hundreds of milliseconds.</summary>
    private static readonly TimeSpan IdentityRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<WindowsNvmlGpuUtilizationReader> _logger;
    private readonly IComputeDeviceIdentityResolver _identityResolver;
    private readonly Lock _syncLock = new();

    private NvmlLibrary? _library;
    private bool _loadAttempted;

    private IReadOnlyList<ComputeDeviceUtilization> _latest = [];
    private long _latestTimestamp;
    private Task? _samplingTask;

    // How long the last NVML sample took. It is the signal for whether the GPU was awake: a fast call queried
    // a running GPU, a slow one woke a sleeping one. See NvmlSamplingBackoff.
    private TimeSpan _lastSampleDuration;
    private long _lastSampleStartedAt;
    private bool _loggedBackoff;

    private long _identitiesResolvedAt;
    private IReadOnlyList<ComputeDeviceIdentity> _identities = [];

    private bool _loggedSampleFailure;
    private bool _disposed;

    public WindowsNvmlGpuUtilizationReader(
        ILogger<WindowsNvmlGpuUtilizationReader> logger,
        IComputeDeviceIdentityResolver identityResolver)
    {
        _logger = logger;
        _identityResolver = identityResolver;
    }

    /// <summary>
    /// True once NVML has loaded. Deliberately does NOT initialise it — that costs the better part of a
    /// second and belongs on the background thread with everything else expensive.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            lock (_syncLock)
            {
                return !_disposed && EnsureLibraryLoaded() is not null;
            }
        }
    }

    /// <summary>
    /// Returns the most recent completed reading and kicks off the next one if none is in flight.
    /// </summary>
    /// <remarks>
    /// Never blocks on NVML. The first call after startup therefore returns nothing — there is no reading
    /// yet — which is the same shape every rate-based reader here has.
    /// </remarks>
    public IReadOnlyList<ComputeDeviceUtilization> Sample()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return [];
            }

            StartSamplingIfIdle();

            // A sampler that stopped producing must go quiet rather than serve its last values forever.
            if (_latestTimestamp != 0 && Stopwatch.GetElapsedTime(_latestTimestamp) > ReadingLifetime)
            {
                return [];
            }

            return _latest;
        }
    }

    public void Dispose()
    {
        Task? pending;

        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _samplingTask;
        }

        // Waited on outside the lock, and bounded: an NVML call that has wedged must not hold up shutdown.
        // The library is freed only once nothing can still be calling into it.
        try
        {
            pending?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "A pending NVML sample did not finish before shutdown.");
        }

        lock (_syncLock)
        {
            _library?.Dispose();
            _library = null;
            _latest = [];
        }
    }

    private void StartSamplingIfIdle()
    {
        if (_samplingTask is { IsCompleted: false })
        {
            return;
        }

        // Ask Windows directly whether the GPU is up, BEFORE the timing heuristic below. This is the cure the
        // backoff is only a mitigation for: a definite low-power answer means the device is left completely
        // alone rather than woken once a minute to prove it is still asleep.
        if (AreAllGpusAsleep())
        {
            if (!_loggedAsleep)
            {
                _loggedAsleep = true;
                _logger.LogDebug("Every NVIDIA GPU reports a low-power device state; skipping NVML entirely until one wakes.");
            }

            return;
        }

        _loggedAsleep = false;

        // Do not disturb a GPU that the last call had to wake. Without this the service pins an idle dGPU
        // awake for as long as it runs — measured at roughly 19 W on the reference machine — to produce
        // telemetry that, with no client attached, nobody is reading.
        //
        // Kept as a second line of defence: the power-state read above returns null on a machine that cannot
        // answer, and this is what protects those.
        if (_lastSampleStartedAt != 0
            && !NvmlSamplingBackoff.ShouldSample(Stopwatch.GetElapsedTime(_lastSampleStartedAt), _lastSampleDuration))
        {
            if (!_loggedBackoff)
            {
                _loggedBackoff = true;
                _logger.LogDebug(
                    "The last NVML sample took {ElapsedMs:F0} ms, which means it woke the GPU; backing off to one sample every {BackoffSeconds:F0} s so it can power down.",
                    _lastSampleDuration.TotalMilliseconds,
                    NvmlSamplingBackoff.SleepingInterval.TotalSeconds);
            }

            return;
        }

        _samplingTask = Task.Run(SampleOnBackgroundThread);
    }

    /// <summary>Logged once per sleep, so a machine that idles for hours does not fill the log.</summary>
    private bool _loggedAsleep;

    /// <summary>
    /// True only when every known GPU definitely reports a low-power device state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately unanimous and deliberately definite. One awake GPU is a reason to sample — the reader
    /// reports them together — and an unknown answer must NOT suppress a read, or a machine whose power state
    /// cannot be queried would report every GPU as permanently idle.
    /// </para>
    /// <para>
    /// Cheap enough for the sampling path: this reads a device property Windows already holds, with no call
    /// into the graphics driver and nothing that can wake the device.
    /// </para>
    /// </remarks>
    private bool AreAllGpusAsleep() => ComputeDeviceSleepGate.AreAllAsleep(_identities);

    private void SampleOnBackgroundThread()
    {
        try
        {
            NvmlLibrary? library;
            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                library = EnsureLibraryLoaded();
            }

            if (library is null || !library.TryInitialize())
            {
                return;
            }

            var startedAt = Stopwatch.GetTimestamp();
            var devices = ReadDevices(library);
            var duration = Stopwatch.GetElapsedTime(startedAt);

            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                _latest = devices;
                _latestTimestamp = Stopwatch.GetTimestamp();
                _lastSampleStartedAt = startedAt;
                _lastSampleDuration = duration;

                // A fast call means the GPU is awake again, so let the next one through immediately.
                if (duration < NvmlSamplingBackoff.WakeCostThreshold)
                {
                    _loggedBackoff = false;
                }
            }

            // Traced per ACTUAL NVML call, not per Sample(): Sample() returns a cached reading on most ticks,
            // so composite-level logging says nothing about how often the GPU is really being touched — which
            // is the number that decides whether a dGPU gets to sleep.
            LogNvmlSampled(duration.TotalMilliseconds, devices.Count, duration >= NvmlSamplingBackoff.WakeCostThreshold);

            {
            }
        }
        catch (Exception exception)
        {
            if (!_loggedSampleFailure)
            {
                _loggedSampleFailure = true;
                _logger.LogWarning(exception, "NVIDIA GPU telemetry could not be sampled; those readings will be unavailable.");
            }
        }
    }

    private List<ComputeDeviceUtilization> ReadDevices(NvmlLibrary library)
    {
        List<ComputeDeviceUtilization> devices = [];

        if (library.TryGetDeviceCount() is not { } count || count == 0)
        {
            return devices;
        }

        var identities = ResolveIdentities();

        for (uint index = 0; index < count; index++)
        {
            var handle = library.TryGetHandleByIndex(index);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            var pciAddress = library.TryGetPciAddress(handle);
            var identity = identities.FirstOrDefault(candidate => WindowsPciAddress.Matches(candidate.PciAddress, pciAddress));

            // Without an OS identity there is no key that lines up with the PDH reader's. Publishing under
            // the PCI address instead would show the GPU twice — once from each reader — which is worse than
            // publishing only what PDH already reports.
            if (identity is null)
            {
                LogUnmatchedDevice(pciAddress ?? "<unknown>");
                continue;
            }

            var utilization = library.TryGetUtilizationPercent(handle);
            var (vramUsed, vramTotal) = library.TryGetMemory(handle);

            devices.Add(new ComputeDeviceUtilization
            {
                DeviceKey = identity.DeviceKey,
                Kind = identity.Kind,
                DisplayName = library.TryGetName(handle) ?? identity.DisplayName,

                // Utilisation intermittently fails on a dGPU changing power state. Zero is the honest answer
                // for a GPU that is powering down, and the composite keeps PDH's figure anyway because that
                // reader publishes first and enrichment never overwrites an answered field.
                UtilizationPercent = utilization ?? 0d,
                PowerWatts = library.TryGetPowerWatts(handle),
                TemperatureCelsius = library.TryGetTemperatureCelsius(handle),
                CoreClockMegahertz = library.TryGetClockMegahertz(handle),
                MaxCoreClockMegahertz = library.TryGetMaxClockMegahertz(handle),
                VramUsedBytes = vramUsed,
                VramTotalBytes = vramTotal,
                ThrottleReasons = library.TryGetThrottleReasons(handle),
            });
        }

        return devices;
    }

    private IReadOnlyList<ComputeDeviceIdentity> ResolveIdentities()
    {
        if (_identities.Count > 0 && Stopwatch.GetElapsedTime(_identitiesResolvedAt) < IdentityRefreshInterval)
        {
            return _identities;
        }

        try
        {
            _identities = _identityResolver.Enumerate();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Enumerating device identities failed; NVIDIA telemetry cannot be matched to a device this round.");
            _identities = [];
        }

        _identitiesResolvedAt = Stopwatch.GetTimestamp();
        return _identities;
    }

    private NvmlLibrary? EnsureLibraryLoaded()
    {
        if (_library is not null)
        {
            return _library;
        }

        // One attempt for the life of the object: a machine without the NVIDIA driver will not grow one, and
        // retrying a failed dlopen on every sample is pure cost.
        if (_loadAttempted)
        {
            return null;
        }

        _loadAttempted = true;
        _library = NvmlLibrary.TryLoad(NvmlLibrary.WindowsCandidates, _logger);

        if (_library is null)
        {
            _logger.LogDebug("nvml.dll was not found; NVIDIA GPU power and temperature will not be reported.");
        }

        return _library;
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "NVML sampled {DeviceCount} device(s) in {ElapsedMs:F2} ms. WokeTheGpu={WokeTheGpu}.")]
    private partial void LogNvmlSampled(double elapsedMs, int deviceCount, bool wokeTheGpu);

    private void LogUnmatchedDevice(string pciAddress)
    {
        _logger.LogDebug(
            "NVML reported a GPU at {PciAddress} with no matching PnP device; its telemetry is not published.",
            pciAddress);
    }
}
