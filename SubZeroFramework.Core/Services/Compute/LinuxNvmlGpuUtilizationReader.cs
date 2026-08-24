using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Reads NVIDIA GPU busyness through NVML, loaded at runtime only if the user already has the driver.
/// </summary>
/// <remarks>
/// NVML is the ONLY source of an NVIDIA utilization percentage on Linux — unlike amdgpu there is no sysfs or
/// procfs attribute for it. It ships with the proprietary driver, so it is loaded with
/// <see cref="NativeLibrary.TryLoad(string, out IntPtr)"/> (a plain dlopen of the soname) and every symbol is
/// resolved defensively. The service therefore has NO link-time or package dependency on anything NVIDIA:
/// on an AMD-only machine the load simply fails and this reader reports nothing, silently.
///
/// POWER, and this is the important part on a Framework 16 with the graphics module: a per-device NVML call
/// takes a runtime-PM reference and can hold a discrete GPU awake, which is exactly how monitoring tools have
/// historically wrecked battery life. So the device list comes from sysfs (free), and NVML is consulted only
/// for a GPU whose <c>power/runtime_status</c> already reads active. A suspended GPU is reported as 0% busy —
/// which is what it is — without being touched.
/// </remarks>
public sealed unsafe class LinuxNvmlGpuUtilizationReader : IComputeUtilizationReader
{
    private const ushort NvidiaVendorId = 0x10DE;
    private const int NvmlSuccess = 0;

    /// <summary>NVML_TEMPERATURE_GPU — the die sensor, the only one nvmlDeviceGetTemperature defines.</summary>
    private const uint NvmlTemperatureGpu = 0;

    /// <summary>NVML_CLOCK_GRAPHICS — the shader clock, which is what "GPU clock" means to a user.</summary>
    private const uint NvmlClockGraphics = 0;

    /// <summary>Big enough for NVML_DEVICE_NAME_V2_BUFFER_SIZE (96); older drivers just write fewer bytes.</summary>
    private const int NameBufferSize = 96;

    /// <summary>
    /// The proprietary module's procfs root. Checked BEFORE dlopen: on a machine where the user deliberately
    /// blacklisted the module, loading NVML as root could trigger an autoload we have no business causing.
    /// </summary>
    private const string NvidiaProcRoot = "/proc/driver/nvidia/version";

    /// <summary>
    /// Load candidates in priority order. The bare soname works wherever ldconfig knows the library, which is
    /// every normal install; the absolute paths cover distributions that place it outside the default search
    /// path (Debian's nvidia/current) or where the cache is stale.
    /// </summary>
    private static readonly string[] LibraryCandidates =
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

    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<LinuxNvmlGpuUtilizationReader> _logger;
    private readonly DrmSysfs _sysfs;
    private readonly string _pciDevicesPath;
    private readonly Stopwatch _sinceDeviceRefresh = Stopwatch.StartNew();

    private IntPtr _library;
    private bool _libraryProbed;
    private bool _initialized;
    private bool _loggedSampleFailure;
    private bool _enforcedPowerLimitResolved;
    private double? _enforcedPowerLimitWatts;
    private bool _maxClockResolved;
    private double? _maxClockMegahertz;

    private IReadOnlyList<NvidiaDevice> _devices = [];
    private bool _enumerated;

    // NVML handles are expensive to discover (each lookup may touch the device), so the PCI-address to
    // handle mapping is built once and reused.
    private readonly Dictionary<string, IntPtr> _handlesByPciAddress = new(StringComparer.OrdinalIgnoreCase);
    private bool _handlesResolved;

    private delegate* unmanaged[Cdecl]<int> _nvmlInit;
    private delegate* unmanaged[Cdecl]<int> _nvmlShutdown;
    private delegate* unmanaged[Cdecl]<uint*, int> _nvmlDeviceGetCount;
    private delegate* unmanaged[Cdecl]<uint, IntPtr*, int> _nvmlDeviceGetHandleByIndex;
    private delegate* unmanaged[Cdecl]<IntPtr, NvmlUtilization*, int> _nvmlDeviceGetUtilizationRates;
    private delegate* unmanaged[Cdecl]<IntPtr, byte*, uint, int> _nvmlDeviceGetName;
    private delegate* unmanaged[Cdecl]<IntPtr, byte*, int> _nvmlDeviceGetPciInfo;

    // Extended telemetry for adaptive fan control. Bound OPTIONALLY: these are resolved separately from the
    // required set above so a driver too old to export one of them still reports utilization, rather than the
    // whole reader going dark over a field that is a refinement.
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, int> _nvmlDeviceGetPowerUsage;
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, int> _nvmlDeviceGetEnforcedPowerLimit;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> _nvmlDeviceGetTemperature;
    private delegate* unmanaged[Cdecl]<IntPtr, ulong*, int> _nvmlDeviceGetCurrentClocksThrottleReasons;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> _nvmlDeviceGetClockInfo;
    private delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> _nvmlDeviceGetMaxClockInfo;
    private delegate* unmanaged[Cdecl]<IntPtr, NvmlMemory*, int> _nvmlDeviceGetMemoryInfo;

    public LinuxNvmlGpuUtilizationReader(
        ILogger<LinuxNvmlGpuUtilizationReader> logger,
        string sysfsRoot = DrmSysfs.DefaultSysfsRoot)
    {
        _logger = logger;
        _sysfs = new DrmSysfs(sysfsRoot);
        _pciDevicesPath = Path.Combine(sysfsRoot, "bus", "pci", "devices");
    }

    /// <summary>True when the machine has an NVIDIA GPU the kernel driver has claimed.</summary>
    /// <remarks>
    /// Deliberately does NOT require NVML to have loaded: a driver present but NVML missing still lets the
    /// device be listed with an unknown reading, which is more honest than hiding the GPU.
    /// </remarks>
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
                // Suspended GPUs are answered from sysfs alone — see the class remarks.
                if (IsRuntimeSuspended(device))
                {
                    samples.Add(Build(device, 0d));
                    continue;
                }

                var utilization = ReadUtilizationPercent(device);
                if (utilization is not null)
                {
                    samples.Add(Build(device, utilization.Value, ReadExtendedTelemetry(device)));
                }
            }

            return samples;
        }
        catch (Exception exception)
        {
            if (!_loggedSampleFailure)
            {
                _loggedSampleFailure = true;
                _logger.LogWarning(exception, "NVIDIA GPU utilization could not be sampled; those devices will report no readings.");
            }

            return [];
        }
    }

    private static ComputeDeviceUtilization Build(
        NvidiaDevice device,
        double utilizationPercent,
        NvmlExtendedTelemetry? extended = null) => new()
    {
        DeviceKey = device.PciAddress,
        Kind = ComputeDeviceKind.Gpu,
        DisplayName = device.DisplayName,
        UtilizationPercent = utilizationPercent,
        PowerWatts = extended?.PowerWatts,
        TemperatureCelsius = extended?.TemperatureCelsius,
        CoreClockMegahertz = extended?.CoreClockMegahertz,
        MaxCoreClockMegahertz = extended?.MaxCoreClockMegahertz,
        VramUsedBytes = extended?.VramUsedBytes,
        VramTotalBytes = extended?.VramTotalBytes,
        ThrottleReasons = extended?.ThrottleReasons,
    };

    /// <summary>
    /// Reads power, temperature, clock and throttle reasons for a device already known to be awake.
    /// </summary>
    /// <remarks>
    /// Only ever called on the non-suspended path. Each call takes a runtime-PM reference, which is exactly
    /// what must not happen to a sleeping discrete GPU — see the class remarks. Every field is independently
    /// optional: a driver that does not export one symbol still reports the rest.
    /// </remarks>
    private NvmlExtendedTelemetry? ReadExtendedTelemetry(NvidiaDevice device)
    {
        var handle = ResolveHandle(device.PciAddress);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var (vramUsed, vramTotal) = ReadMemory(handle);

        return new NvmlExtendedTelemetry(
            PowerWatts: ReadPowerWatts(handle),
            TemperatureCelsius: ReadTemperatureCelsius(handle),
            CoreClockMegahertz: ReadPlausibleClockMegahertz(handle),
            MaxCoreClockMegahertz: ReadMaxClockMegahertz(handle),
            ThrottleReasons: ReadThrottleReasons(handle),
            VramUsedBytes: vramUsed,
            VramTotalBytes: vramTotal);
    }


    /// <summary>Current core clock, rejected when it exceeds the device's stated maximum.</summary>
    private double? ReadPlausibleClockMegahertz(IntPtr handle)
    {
        var megahertz = ReadClockMegahertz(_nvmlDeviceGetClockInfo, handle);
        return megahertz is { } value && NvmlReadingPlausibility.IsClockPlausible(value, ReadMaxClockMegahertz(handle))
            ? value
            : null;
    }

    /// <summary>The device's maximum core clock, cached — it is the divisor for every clock reading.</summary>
    private double? ReadMaxClockMegahertz(IntPtr handle)
    {
        if (_maxClockResolved)
        {
            return _maxClockMegahertz;
        }

        _maxClockResolved = true;
        _maxClockMegahertz = ReadClockMegahertz(_nvmlDeviceGetMaxClockInfo, handle);
        return _maxClockMegahertz;
    }

    /// <summary>
    /// Board power in watts, or null when it could not be read OR the reading cannot be believed.
    /// </summary>
    /// <remarks>
    /// NVML returns garbage with <c>NVML_SUCCESS</c> on a laptop dGPU changing power state, so the status code
    /// cannot filter it. Measured on a Framework 16 RTX 5070 under Windows — same silicon, same driver family,
    /// so the guard belongs here too. See <see cref="NvmlReadingPlausibility"/>.
    /// </remarks>
    private double? ReadPowerWatts(IntPtr handle)
    {
        if (_nvmlDeviceGetPowerUsage is null)
        {
            return null;
        }

        uint milliwatts;
        if (_nvmlDeviceGetPowerUsage(handle, &milliwatts) != NvmlSuccess)
        {
            return null;
        }

        var watts = milliwatts / 1000d;
        return NvmlReadingPlausibility.IsPlausible(watts, ReadEnforcedPowerLimitWatts(handle)) ? watts : null;
    }

    /// <summary>The device's enforced power limit, cached because it does not change while the machine runs.</summary>
    private double? ReadEnforcedPowerLimitWatts(IntPtr handle)
    {
        if (_enforcedPowerLimitResolved)
        {
            return _enforcedPowerLimitWatts;
        }

        _enforcedPowerLimitResolved = true;

        if (_nvmlDeviceGetEnforcedPowerLimit is not null)
        {
            uint milliwatts;
            if (_nvmlDeviceGetEnforcedPowerLimit(handle, &milliwatts) == NvmlSuccess && milliwatts > 0)
            {
                _enforcedPowerLimitWatts = milliwatts / 1000d;
            }
        }

        return _enforcedPowerLimitWatts;
    }

    private double? ReadTemperatureCelsius(IntPtr handle)
    {
        if (_nvmlDeviceGetTemperature is null)
        {
            return null;
        }

        uint celsius;
        return _nvmlDeviceGetTemperature(handle, NvmlTemperatureGpu, &celsius) == NvmlSuccess ? celsius : null;
    }

    /// <summary>Reads a graphics clock through whichever NVML clock entry point is passed.</summary>
    private static double? ReadClockMegahertz(delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int> entryPoint, IntPtr handle)
    {
        if (entryPoint is null)
        {
            return null;
        }

        uint megahertz;
        return entryPoint(handle, NvmlClockGraphics, &megahertz) == NvmlSuccess ? megahertz : null;
    }

    /// <summary>
    /// The throttle bitmask, or null when it could not be read.
    /// </summary>
    /// <remarks>
    /// Null and <see cref="ComputeThrottleReasons.None"/> are NOT the same answer here: None means NVML
    /// replied and nothing is holding the clocks back, while null means the question could not be asked. The
    /// controller escalates on the first and must not on the second.
    /// </remarks>
    private ComputeThrottleReasons? ReadThrottleReasons(IntPtr handle)
    {
        if (_nvmlDeviceGetCurrentClocksThrottleReasons is null)
        {
            return null;
        }

        ulong bitmask;
        return _nvmlDeviceGetCurrentClocksThrottleReasons(handle, &bitmask) == NvmlSuccess
            ? NvmlThrottleReasons.Map(bitmask)
            : null;
    }

    private readonly record struct NvmlExtendedTelemetry(
        double? PowerWatts,
        double? TemperatureCelsius,
        double? CoreClockMegahertz,
        double? MaxCoreClockMegahertz,
        ComputeThrottleReasons? ThrottleReasons,
        double? VramUsedBytes,
        double? VramTotalBytes);

    /// <summary>Video memory used and total, in bytes.</summary>
    private (double? UsedBytes, double? TotalBytes) ReadMemory(IntPtr handle)
    {
        if (_nvmlDeviceGetMemoryInfo is null)
        {
            return (null, null);
        }

        NvmlMemory memory;
        if (_nvmlDeviceGetMemoryInfo(handle, &memory) != NvmlSuccess || memory.Total == 0)
        {
            return (null, null);
        }

        return (memory.Used, memory.Total);
    }

    private double? ReadUtilizationPercent(NvidiaDevice device)
    {
        if (!EnsureInitialized())
        {
            return null;
        }

        var handle = ResolveHandle(device.PciAddress);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        NvmlUtilization utilization;
        var status = _nvmlDeviceGetUtilizationRates(handle, &utilization);
        if (status != NvmlSuccess)
        {
            // A GPU that fell asleep between the sysfs check and this call, or was hot-unplugged, lands here.
            return null;
        }

        return Math.Clamp(utilization.Gpu, 0u, 100u);
    }

    private bool IsRuntimeSuspended(NvidiaDevice device) =>
        string.Equals(DrmSysfs.ReadAttribute(device.RuntimeStatusPath), "suspended", StringComparison.OrdinalIgnoreCase);

    // ----- device enumeration (sysfs only; never wakes anything) -----

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
            _devices = EnumerateDevices();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Enumerating NVIDIA devices failed; NVIDIA GPU utilization will be unavailable.");
            _devices = [];
        }
    }

    private IReadOnlyList<NvidiaDevice> EnumerateDevices()
    {
        // The DRM tree covers nouveau and the open kernel module; the proprietary driver does not always
        // register a DRM card, so PCI is the reliable enumeration and DRM only enriches it.
        List<(string PciAddress, ushort DeviceId)> candidates = [];

        if (Directory.Exists(_pciDevicesPath))
        {
            foreach (var directory in Directory.EnumerateDirectories(_pciDevicesPath))
            {
                var address = Path.GetFileName(directory);
                if (DrmSysfs.ReadHexIdAttribute(Path.Combine(directory, "vendor")) != NvidiaVendorId)
                {
                    continue;
                }

                // Class 0x03xxxx is a display controller; 0x0302 covers the 3D-controller form a laptop
                // discrete GPU usually takes.
                var deviceClass = DrmSysfs.ReadAttribute(Path.Combine(directory, "class"));
                if (deviceClass is null || !deviceClass.StartsWith("0x03", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var deviceId = DrmSysfs.ReadHexIdAttribute(Path.Combine(directory, "device"));
                candidates.Add((address, deviceId ?? 0));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var pciNames = PciIdDatabase.Lookup([.. candidates.Select(candidate => new PciDeviceId(NvidiaVendorId, candidate.DeviceId)).Distinct()]);

        List<NvidiaDevice> devices = [];
        foreach (var (address, deviceId) in candidates.OrderBy(candidate => candidate.PciAddress, StringComparer.Ordinal))
        {
            var name = pciNames.GetValueOrDefault(new PciDeviceId(NvidiaVendorId, deviceId))?.DeviceName;

            devices.Add(new NvidiaDevice
            {
                PciAddress = address,
                DisplayName = name ?? $"NVIDIA GPU ({address})",
                RuntimeStatusPath = Path.Combine(_pciDevicesPath, address, "power", "runtime_status"),
            });
        }

        _logger.LogInformation(
            "NVIDIA GPU utilization: {Count} device(s) found ({Devices}); readings require the NVIDIA driver's NVML library.",
            devices.Count,
            string.Join(", ", devices.Select(device => device.DisplayName)));

        return devices;
    }

    // ----- NVML loading -----

    private bool EnsureInitialized()
    {
        if (_initialized)
        {
            return true;
        }

        if (!TryLoadLibrary())
        {
            return false;
        }

        var status = _nvmlInit();
        if (status != NvmlSuccess)
        {
            // NVML_ERROR_DRIVER_NOT_LOADED (9) is the ordinary "no NVIDIA driver running" case.
            _logger.LogInformation("NVML initialisation returned {Status}; NVIDIA GPU utilization will not be reported.", status);
            return false;
        }

        _initialized = true;
        return true;
    }

    private bool TryLoadLibrary()
    {
        if (_libraryProbed)
        {
            return _library != IntPtr.Zero;
        }

        _libraryProbed = true;

        // No kernel module means NVML can only fail — and probing it as root could autoload the module.
        if (!File.Exists(NvidiaProcRoot))
        {
            _logger.LogDebug("No {ProcPath}; skipping NVML entirely.", NvidiaProcRoot);
            return false;
        }

        foreach (var candidate in LibraryCandidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                _library = handle;
                break;
            }
        }

        if (_library == IntPtr.Zero)
        {
            _logger.LogInformation(
                "The NVIDIA driver is loaded but its NVML library was not found; install the driver's NVML package (nvidia-utils / libnvidia-compute) for GPU utilization.");
            return false;
        }

        if (!TryBindSymbols())
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves every entry point. All are required; a library missing any of them is one we do not
    /// understand, and guessing at a partial ABI in a root process is not worth a utilization percentage.
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
            _logger.LogInformation("The NVML library is missing expected entry points; NVIDIA GPU utilization will not be reported.");
            return false;
        }

        _nvmlInit = (delegate* unmanaged[Cdecl]<int>)init;
        _nvmlShutdown = (delegate* unmanaged[Cdecl]<int>)shutdown;
        _nvmlDeviceGetCount = (delegate* unmanaged[Cdecl]<uint*, int>)getCount;
        _nvmlDeviceGetHandleByIndex = (delegate* unmanaged[Cdecl]<uint, IntPtr*, int>)getHandle;
        _nvmlDeviceGetUtilizationRates = (delegate* unmanaged[Cdecl]<IntPtr, NvmlUtilization*, int>)getUtilization;
        _nvmlDeviceGetName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, uint, int>)getName;
        _nvmlDeviceGetPciInfo = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int>)getPciInfo;

        // Optional: a missing symbol leaves the pointer null and the corresponding field unreported.
        if (TryGet("nvmlDeviceGetPowerUsage", out var getPower))
        {
            _nvmlDeviceGetPowerUsage = (delegate* unmanaged[Cdecl]<IntPtr, uint*, int>)getPower;
        }

        if (TryGet("nvmlDeviceGetEnforcedPowerLimit", out var getEnforcedLimit))
        {
            _nvmlDeviceGetEnforcedPowerLimit = (delegate* unmanaged[Cdecl]<IntPtr, uint*, int>)getEnforcedLimit;
        }

        if (TryGet("nvmlDeviceGetTemperature", out var getTemperature))
        {
            _nvmlDeviceGetTemperature = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int>)getTemperature;
        }

        if (TryGet("nvmlDeviceGetCurrentClocksThrottleReasons", out var getThrottle))
        {
            _nvmlDeviceGetCurrentClocksThrottleReasons = (delegate* unmanaged[Cdecl]<IntPtr, ulong*, int>)getThrottle;
        }

        if (TryGet("nvmlDeviceGetClockInfo", out var getClock))
        {
            _nvmlDeviceGetClockInfo = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int>)getClock;
        }

        if (TryGet("nvmlDeviceGetMaxClockInfo", out var getMaxClock))
        {
            _nvmlDeviceGetMaxClockInfo = (delegate* unmanaged[Cdecl]<IntPtr, uint, uint*, int>)getMaxClock;
        }

        // The unsuffixed name is the v1 layout (total/free/used), which is what NvmlMemory declares. The _v2
        // entry point adds reserved/version fields in a LARGER struct — binding it against this layout would
        // have NVML write past the end, so the plain symbol is the correct one here.
        if (TryGet("nvmlDeviceGetMemoryInfo", out var getMemory))
        {
            _nvmlDeviceGetMemoryInfo = (delegate* unmanaged[Cdecl]<IntPtr, NvmlMemory*, int>)getMemory;
        }

        return true;
    }

    private bool TryGet(string symbol, out IntPtr address) => NativeLibrary.TryGetExport(_library, symbol, out address);

    /// <summary>
    /// Maps a sysfs PCI address to its NVML handle, building the whole map on first use.
    /// </summary>
    /// <remarks>
    /// Called only once at least one GPU is already awake, so the per-device NVML calls this makes cannot be
    /// what woke it.
    /// </remarks>
    private IntPtr ResolveHandle(string pciAddress)
    {
        if (_handlesByPciAddress.TryGetValue(pciAddress, out var cached))
        {
            return cached;
        }

        if (_handlesResolved)
        {
            return IntPtr.Zero;
        }

        _handlesResolved = true;

        uint count;
        if (_nvmlDeviceGetCount(&count) != NvmlSuccess)
        {
            return IntPtr.Zero;
        }

        // nvmlPciInfo_t is 68 bytes in the _v3 layout; over-allocating costs nothing and makes a layout
        // surprise in a future driver a wasted read rather than a smashed stack in a root process.
        const int PciInfoBufferSize = 256;
        var buffer = stackalloc byte[PciInfoBufferSize];

        for (uint index = 0; index < count; index++)
        {
            IntPtr handle;
            if (_nvmlDeviceGetHandleByIndex(index, &handle) != NvmlSuccess)
            {
                continue;
            }

            new Span<byte>(buffer, PciInfoBufferSize).Clear();
            if (_nvmlDeviceGetPciInfo(handle, buffer) != NvmlSuccess)
            {
                continue;
            }

            // Field 0 of nvmlPciInfo_t is busIdLegacy[16], NUL-terminated, in "0000:01:00.0" form — the same
            // shape sysfs uses, so it maps directly onto the device key.
            var legacyBusId = ReadAsciiZ(buffer, 16);
            if (!string.IsNullOrWhiteSpace(legacyBusId))
            {
                _handlesByPciAddress[legacyBusId] = handle;
            }
        }

        return _handlesByPciAddress.GetValueOrDefault(pciAddress, IntPtr.Zero);
    }

    /// <summary>Reads the marketing name for a handle; used only for logging, so failure is non-fatal.</summary>
    private string? ReadDeviceName(IntPtr handle)
    {
        var buffer = stackalloc byte[NameBufferSize];
        return _nvmlDeviceGetName(handle, buffer, NameBufferSize) == NvmlSuccess
            ? ReadAsciiZ(buffer, NameBufferSize)
            : null;
    }

    private static string ReadAsciiZ(byte* buffer, int maxLength)
    {
        var length = 0;
        while (length < maxLength && buffer[length] != 0)
        {
            length++;
        }

        return length == 0 ? string.Empty : Encoding.ASCII.GetString(buffer, length);
    }

    public void Dispose()
    {
        try
        {
            if (_initialized && _nvmlShutdown is not null)
            {
                _nvmlShutdown();
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "nvmlShutdown failed.");
        }
        finally
        {
            _initialized = false;

            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
                _library = IntPtr.Zero;
            }
        }
    }

    /// <summary>nvmlUtilization_t: two percentages. "memory" is controller duty cycle, NOT VRAM used.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    /// <summary>nvmlMemory_t (v1): total, free and used video memory in bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    private sealed record NvidiaDevice
    {
        public required string PciAddress { get; init; }

        public required string DisplayName { get; init; }

        public required string RuntimeStatusPath { get; init; }
    }
}
