using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// A loaded NVML library and its entry points.
/// </summary>
/// <remarks>
/// <para>
/// Used by the Windows reader today. <c>LinuxNvmlGpuUtilizationReader</c> still carries its own copy of this
/// binding and should migrate onto it — deliberately not done in the same change, because that reader cannot
/// be exercised without an NVIDIA GPU running Linux, and refactoring untestable interop is how a working
/// reader quietly stops working. The plausibility guards were applied to both in the meantime, so the two
/// agree on behaviour even while the code is duplicated.
/// </para>
/// <para>
/// Only the BINDING lives here. Which devices exist, and whether it is safe to touch one, is policy that
/// differs per platform and stays with each reader: Linux enumerates from sysfs and consults NVML only for a
/// GPU whose <c>power/runtime_status</c> already reads active, because a per-device call takes a runtime-PM
/// reference and can hold a discrete GPU awake.
/// </para>
/// <para>
/// That hazard is not Linux-only. Measured on a Framework 16 with an RTX 5070 under Windows: most calls cost
/// 0.02 ms, but roughly every third one stalls 480-590 ms and returns <c>NVML_ERROR_UNKNOWN</c> for
/// utilisation, with board power jumping 19 W to 29 W on exactly those calls — the GPU changing power state.
/// Nothing here may therefore be called from a polling tier's own thread.
/// </para>
/// <para>
/// The required entry points are all-or-nothing: a library missing one is a library whose ABI we do not
/// understand, and guessing at a partial ABI in a privileged process is not worth a utilisation percentage.
/// The extended telemetry entry points are individually optional, so an older driver still reports what it can.
/// </para>
/// </remarks>
public sealed unsafe class NvmlLibrary : IDisposable
{
    private const int NvmlSuccess = 0;

    /// <summary>NVML_TEMPERATURE_GPU — the die sensor, the only one nvmlDeviceGetTemperature defines.</summary>
    private const uint NvmlTemperatureGpu = 0;

    /// <summary>NVML_CLOCK_GRAPHICS — the shader clock, which is what "GPU clock" means to a user.</summary>
    private const uint NvmlClockGraphics = 0;

    /// <summary>Big enough for NVML_DEVICE_NAME_V2_BUFFER_SIZE (96); older drivers just write fewer bytes.</summary>
    private const int NameBufferSize = 96;

    /// <summary>nvmlPciInfo_t is 68 bytes in the _v3 layout; over-allocating makes a layout change harmless.</summary>
    private const int PciInfoBufferSize = 128;

    /// <summary>busIdLegacy is the first field, a NUL-terminated 16-byte "0000:01:00.0".</summary>
    private const int PciBusIdLength = 16;

    /// <summary>
    /// Linux load candidates in priority order. The bare soname works wherever ldconfig knows the library;
    /// the absolute paths cover distributions that place it outside the default search path.
    /// </summary>
    public static IReadOnlyList<string> LinuxCandidates { get; } =
    [
        "libnvidia-ml.so.1",
        "/usr/lib/x86_64-linux-gnu/libnvidia-ml.so.1",
        "/usr/lib/x86_64-linux-gnu/nvidia/current/libnvidia-ml.so.1",
        "/usr/lib64/libnvidia-ml.so.1",
        "/usr/lib64/nvidia/libnvidia-ml.so.1",
        "/usr/lib/libnvidia-ml.so.1",
        "/usr/lib/aarch64-linux-gnu/libnvidia-ml.so.1",
        // Unversioned symlink last: it is a developer-package artefact and absent on Debian/Ubuntu runtimes.
        "libnvidia-ml.so",
    ];

    /// <summary>
    /// Windows load candidates. The driver installs nvml.dll into System32, so the bare name resolves;
    /// verified present on the reference machine.
    /// </summary>
    public static IReadOnlyList<string> WindowsCandidates { get; } = ["nvml.dll"];

    private readonly ILogger _logger;
    private IntPtr _library;
    private bool _initialized;
    private bool _disposed;
    private bool _loggedImplausiblePower;
    private bool _loggedImplausibleClock;
    private bool _maxClockResolved;
    private double? _maxClockMegahertz;
    private bool _enforcedPowerLimitResolved;
    private double? _enforcedPowerLimitWatts;

    private delegate* unmanaged[Cdecl]<int> _init;
    private delegate* unmanaged[Cdecl]<int> _shutdown;
    private delegate* unmanaged[Cdecl]<uint*, int> _getCount;
    private delegate* unmanaged[Cdecl]<uint, IntPtr*, int> _getHandleByIndex;
    private delegate* unmanaged[Cdecl]<IntPtr, NvmlUtilization*, int> _getUtilizationRates;
    private delegate* unmanaged[Cdecl]<IntPtr, byte*, uint, int> _getName;
    private delegate* unmanaged[Cdecl]<IntPtr, byte*, int> _getPciInfo;

    // Optional: bound individually so a driver missing one still reports the rest.
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, int> _getPowerUsage;
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, int> _getEnforcedPowerLimit;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> _getTemperature;
    private delegate* unmanaged[Cdecl]<IntPtr, ulong*, int> _getThrottleReasons;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> _getClockInfo;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> _getMaxClockInfo;
    private delegate* unmanaged[Cdecl]<IntPtr, NvmlMemory*, int> _getMemoryInfo;

    private NvmlLibrary(ILogger logger, IntPtr library)
    {
        _logger = logger;
        _library = library;
    }

    /// <summary>The candidate list for the platform this process is running on.</summary>
    public static IReadOnlyList<string> DefaultCandidates
        => OperatingSystem.IsWindows() ? WindowsCandidates : LinuxCandidates;

    /// <summary>
    /// Loads NVML and binds its entry points, or returns null when it is absent or unrecognised.
    /// </summary>
    /// <remarks>
    /// Does NOT call <c>nvmlInit</c> — loading is cheap, initialising is not (measured 385-870 ms on the
    /// reference machine), so that is deferred to <see cref="TryInitialize"/> and paid once by whoever
    /// actually samples.
    /// </remarks>
    public static NvmlLibrary? TryLoad(IReadOnlyList<string> candidates, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var candidate in candidates)
        {
            // Trusted load, NOT a bare-name search: this runs as LocalSystem and a bare name ends at %PATH%.
            if (!TrustedNativeLibrary.TryLoad(candidate, out var handle))
            {
                continue;
            }

            var library = new NvmlLibrary(logger, handle);
            if (library.TryBindSymbols())
            {
                return library;
            }

            library.Dispose();
            return null;
        }

        return null;
    }

    /// <summary>
    /// Calls <c>nvmlInit_v2</c> once. Expensive — see the remarks on <see cref="TryLoad"/>.
    /// </summary>
    public bool TryInitialize()
    {
        if (_disposed)
        {
            return false;
        }

        if (_initialized)
        {
            return true;
        }

        var status = _init();
        if (status != NvmlSuccess)
        {
            // NVML_ERROR_DRIVER_NOT_LOADED (9) is the ordinary "no NVIDIA driver running" case.
            _logger.LogInformation("NVML initialisation returned {Status}; NVIDIA GPU telemetry will not be reported.", status);
            return false;
        }

        _initialized = true;
        return true;
    }

    /// <summary>How many NVIDIA devices NVML can see, or null when it could not say.</summary>
    public uint? TryGetDeviceCount()
    {
        uint count;
        return _getCount(&count) == NvmlSuccess ? count : null;
    }

    /// <summary>The opaque device handle for an index, or <see cref="IntPtr.Zero"/>.</summary>
    public IntPtr TryGetHandleByIndex(uint index)
    {
        IntPtr handle;
        return _getHandleByIndex(index, &handle) == NvmlSuccess ? handle : IntPtr.Zero;
    }

    public string? TryGetName(IntPtr handle)
    {
        var buffer = stackalloc byte[NameBufferSize];
        return _getName(handle, buffer, NameBufferSize) == NvmlSuccess
            ? Encoding.UTF8.GetString(buffer, NameBufferSize).TrimEnd('\0')
            : null;
    }

    /// <summary>
    /// The device's PCI address, lower-cased so it compares equal to
    /// <see cref="WindowsPciAddress.Format"/> and to a Linux sysfs slot name.
    /// </summary>
    public string? TryGetPciAddress(IntPtr handle)
    {
        var buffer = stackalloc byte[PciInfoBufferSize];
        if (_getPciInfo(handle, buffer) != NvmlSuccess)
        {
            return null;
        }

        // Field 0 of nvmlPciInfo_t is busIdLegacy[16], NUL-terminated, in "0000:01:00.0" form.
        var address = Encoding.UTF8.GetString(buffer, PciBusIdLength).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(address) ? null : address.ToLowerInvariant();
    }

    /// <summary>Busy share, 0-100.</summary>
    public double? TryGetUtilizationPercent(IntPtr handle)
    {
        NvmlUtilization utilization;
        return _getUtilizationRates(handle, &utilization) == NvmlSuccess
            ? Math.Clamp(utilization.Gpu, 0u, 100u)
            : null;
    }

    /// <summary>
    /// Board power in watts, or null when it could not be read OR the reading cannot be believed.
    /// </summary>
    /// <remarks>
    /// The plausibility check is not defensive programming for its own sake: NVML returns garbage with
    /// <c>NVML_SUCCESS</c> on a laptop dGPU changing power state, so the status code cannot filter it. See
    /// <see cref="NvmlReadingPlausibility"/> for the measurements behind the bound.
    /// </remarks>
    public double? TryGetPowerWatts(IntPtr handle)
    {
        if (_getPowerUsage is null)
        {
            return null;
        }

        uint milliwatts;
        if (_getPowerUsage(handle, &milliwatts) != NvmlSuccess)
        {
            return null;
        }

        var watts = milliwatts / 1000d;
        if (NvmlReadingPlausibility.IsPlausible(watts, TryGetEnforcedPowerLimitWatts(handle)))
        {
            return watts;
        }

        if (!_loggedImplausiblePower)
        {
            _loggedImplausiblePower = true;
            _logger.LogDebug(
                "NVML reported {Watts:F1} W against an enforced limit of {Limit}; the reading was discarded. This is expected while a laptop dGPU changes power state.",
                watts,
                TryGetEnforcedPowerLimitWatts(handle)?.ToString("F1") ?? "unknown");
        }

        return null;
    }

    /// <summary>
    /// The device's enforced power limit in watts, cached because it does not change while the machine runs.
    /// </summary>
    /// <remarks>
    /// Cached per LIBRARY rather than per handle: every reader here drives a single discrete GPU, and looking
    /// the limit up on every power read would double the number of NVML calls — each of which can be one of
    /// the slow ones.
    /// </remarks>
    private double? TryGetEnforcedPowerLimitWatts(IntPtr handle)
    {
        if (_enforcedPowerLimitResolved)
        {
            return _enforcedPowerLimitWatts;
        }

        _enforcedPowerLimitResolved = true;

        if (_getEnforcedPowerLimit is not null)
        {
            uint milliwatts;
            if (_getEnforcedPowerLimit(handle, &milliwatts) == NvmlSuccess && milliwatts > 0)
            {
                _enforcedPowerLimitWatts = milliwatts / 1000d;
            }
        }

        return _enforcedPowerLimitWatts;
    }

    public double? TryGetTemperatureCelsius(IntPtr handle)
    {
        if (_getTemperature is null)
        {
            return null;
        }

        uint celsius;
        return _getTemperature(handle, NvmlTemperatureGpu, &celsius) == NvmlSuccess ? celsius : null;
    }

    /// <summary>
    /// Current core clock, or null when it could not be read OR exceeds the device's stated maximum.
    /// </summary>
    /// <remarks>
    /// The same mid-transition garbage that corrupts power reaches the clock — see
    /// <see cref="NvmlReadingPlausibility.IsClockPlausible"/>.
    /// </remarks>
    public double? TryGetClockMegahertz(IntPtr handle)
    {
        if (ReadClock(_getClockInfo, handle) is not { } megahertz)
        {
            return null;
        }

        if (NvmlReadingPlausibility.IsClockPlausible(megahertz, TryGetMaxClockMegahertz(handle)))
        {
            return megahertz;
        }

        if (!_loggedImplausibleClock)
        {
            _loggedImplausibleClock = true;
            _logger.LogDebug(
                "NVML reported a {Megahertz:F0} MHz core clock above the device maximum; the reading was discarded. This is expected while a laptop dGPU changes power state.",
                megahertz);
        }

        return null;
    }

    /// <summary>
    /// The device's maximum core clock, cached because it does not change while the machine runs.
    /// </summary>
    /// <remarks>
    /// Cached for the same reason the power limit is: it is the divisor for every clock reading, and looking
    /// it up per sample would double the NVML calls, each of which can be one of the slow ones.
    /// </remarks>
    public double? TryGetMaxClockMegahertz(IntPtr handle)
    {
        if (_maxClockResolved)
        {
            return _maxClockMegahertz;
        }

        _maxClockResolved = true;
        _maxClockMegahertz = ReadClock(_getMaxClockInfo, handle);
        return _maxClockMegahertz;
    }

    /// <summary>
    /// Why the device is clocked below its rating, or null when the bitmask could not be read.
    /// </summary>
    /// <remarks>
    /// Null and <see cref="ComputeThrottleReasons.None"/> are different answers: None means NVML replied and
    /// nothing is holding the clocks back, null means the question could not be asked.
    /// </remarks>
    public ComputeThrottleReasons? TryGetThrottleReasons(IntPtr handle)
    {
        if (_getThrottleReasons is null)
        {
            return null;
        }

        ulong bitmask;
        return _getThrottleReasons(handle, &bitmask) == NvmlSuccess ? NvmlThrottleReasons.Map(bitmask) : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_initialized && _shutdown is not null)
            {
                _shutdown();
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "NVML shutdown failed.");
        }

        _initialized = false;

        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    private static double? ReadClock(delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> entryPoint, IntPtr handle)
    {
        if (entryPoint is null)
        {
            return null;
        }

        uint megahertz;
        return entryPoint(handle, NvmlClockGraphics, &megahertz) == NvmlSuccess ? megahertz : null;
    }

    /// <summary>
    /// Resolves every entry point. The first group is required; the extended-telemetry group is optional.
    /// </summary>
    private bool TryBindSymbols()
    {
        // The _v2/_v3 suffixes ARE the exported names for the modern ABI — the unsuffixed aliases exist but
        // are pinned to older struct layouts.
        if (!TryGet("nvmlInit_v2", out var init)
            || !TryGet("nvmlShutdown", out var shutdown)
            || !TryGet("nvmlDeviceGetCount_v2", out var getCount)
            || !TryGet("nvmlDeviceGetHandleByIndex_v2", out var getHandle)
            || !TryGet("nvmlDeviceGetUtilizationRates", out var getUtilization)
            || !TryGet("nvmlDeviceGetName", out var getName)
            || !TryGet("nvmlDeviceGetPciInfo_v3", out var getPciInfo))
        {
            _logger.LogInformation("The NVML library is missing expected entry points; NVIDIA GPU telemetry will not be reported.");
            return false;
        }

        _init = (delegate* unmanaged[Cdecl]<int>)init;
        _shutdown = (delegate* unmanaged[Cdecl]<int>)shutdown;
        _getCount = (delegate* unmanaged[Cdecl]<uint*, int>)getCount;
        _getHandleByIndex = (delegate* unmanaged[Cdecl]<uint, IntPtr*, int>)getHandle;
        _getUtilizationRates = (delegate* unmanaged[Cdecl]<IntPtr, NvmlUtilization*, int>)getUtilization;
        _getName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, uint, int>)getName;
        _getPciInfo = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int>)getPciInfo;

        if (TryGet("nvmlDeviceGetPowerUsage", out var getPower))
        {
            _getPowerUsage = (delegate* unmanaged[Cdecl]<IntPtr, uint*, int>)getPower;
        }

        if (TryGet("nvmlDeviceGetEnforcedPowerLimit", out var getEnforcedLimit))
        {
            _getEnforcedPowerLimit = (delegate* unmanaged[Cdecl]<IntPtr, uint*, int>)getEnforcedLimit;
        }

        if (TryGet("nvmlDeviceGetTemperature", out var getTemperature))
        {
            _getTemperature = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int>)getTemperature;
        }

        if (TryGet("nvmlDeviceGetCurrentClocksThrottleReasons", out var getThrottle))
        {
            _getThrottleReasons = (delegate* unmanaged[Cdecl]<IntPtr, ulong*, int>)getThrottle;
        }

        if (TryGet("nvmlDeviceGetClockInfo", out var getClock))
        {
            _getClockInfo = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int>)getClock;
        }

        if (TryGet("nvmlDeviceGetMaxClockInfo", out var getMaxClock))
        {
            _getMaxClockInfo = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int>)getMaxClock;
        }

        // The unsuffixed name deliberately, not _v2: v1's nvmlMemory_t is {total, free, used} and has been
        // stable for years, while v2 prepends a version field and adds a reserved block. Binding the older
        // shape means the struct below cannot silently disagree with the driver.
        if (TryGet("nvmlDeviceGetMemoryInfo", out var getMemory))
        {
            _getMemoryInfo = (delegate* unmanaged[Cdecl]<IntPtr, NvmlMemory*, int>)getMemory;
        }

        return true;
    }

    private bool TryGet(string name, out IntPtr address)
        => NativeLibrary.TryGetExport(_library, name, out address);

    /// <summary>Video memory used and total, in bytes, or nulls when NVML could not report them.</summary>
    public (double? UsedBytes, double? TotalBytes) TryGetMemory(IntPtr handle)
    {
        if (_getMemoryInfo is null)
        {
            return (null, null);
        }

        NvmlMemory memory;
        if (_getMemoryInfo(handle, &memory) != NvmlSuccess || memory.Total == 0)
        {
            return (null, null);
        }

        return (memory.Used, memory.Total);
    }

    /// <summary>nvmlMemory_t (v1): total, free and used video memory in bytes, in that order.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    /// <summary>nvmlUtilization_t: busy share of the GPU and of its memory interface, each 0-100.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }
}
