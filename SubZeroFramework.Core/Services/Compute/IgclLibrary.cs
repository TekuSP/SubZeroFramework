using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Extensions.Logging;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// A loaded Intel Graphics Control Library (IGCL) and the entry points this app uses.
/// </summary>
/// <remarks>
/// <para>
/// IGCL is Intel's public control/telemetry API, shipped as <c>ControlLib.dll</c> with the graphics driver
/// since 2022 — the Intel counterpart to NVML and ADLX. Unlike ADLX it is a FLAT C API (every function is a
/// plain <c>__cdecl</c> export), so there is no vtable navigation to get wrong; the risk concentrates in the
/// struct layouts, which were transcribed field-by-field from the published <c>igcl_api.h</c>
/// (github.com/intel/drivers.gpu.control-library) and are guarded by size assertions at load time. A layout
/// this code computed differently from the header would make IGCL reject the call with an error rather than
/// corrupt memory — every IGCL struct opens with a <c>Size</c> field the implementation validates — but the
/// assertions turn that into a clear log line instead of a silent absence.
/// </para>
/// <para>
/// NOT verified against hardware: the reference machine has no Intel GPU. Everything here therefore fails
/// SOFT — a missing DLL, a failed init, a rejected struct size, or an error from any call degrades to "no
/// Intel telemetry" while PDH continues to report the adapter's utilisation.
/// </para>
/// <para>
/// Only the BINDING lives here; device policy (identity joining, counter deltas, when to sample) stays with
/// the reader, mirroring the NVML split.
/// </para>
/// </remarks>
public sealed unsafe class IgclLibrary : IDisposable
{
    private const int CtlResultSuccess = 0;

    /// <summary>CTL_MAKE_VERSION(1, 1) — the oldest API generation that carries everything used here.</summary>
    private const uint AppVersion = (1u << 16) | 1u;

    /// <summary>CTL_INIT_FLAG_USE_LEVEL_ZERO — "usually required for telemetry, performance, frequency related APIs".</summary>
    private const uint InitFlagUseLevelZero = 1u;

    /// <summary>CTL_FREQ_DOMAIN_GPU — the core clock domain, as opposed to memory or media.</summary>
    private const uint FreqDomainGpu = 0;

    /// <summary>Stack-allocation guard: no sane machine has more handles than this per enumeration.</summary>
    private const uint MaximumHandles = 64;

    /// <summary>CTL_TEMP_SENSORS_GLOBAL — the maximum across every sensor on the device (the hotspot).</summary>
    private const uint TempSensorGlobal = 0;

    /// <summary>CTL_TEMP_SENSORS_GPU — the maximum across the GPU's own sensors.</summary>
    private const uint TempSensorGpu = 1;

    /// <summary>The loader the Intel driver installs into System32. Absent → no Intel GPU (or a pre-2022 driver).</summary>
    public static IReadOnlyList<string> WindowsCandidates { get; } = ["ControlLib.dll"];

    private readonly ILogger _logger;
    private IntPtr _library;
    private IntPtr _apiHandle;
    private bool _disposed;

    private delegate* unmanaged[Cdecl]<CtlInitArgs*, IntPtr*, int> _init;
    private delegate* unmanaged[Cdecl]<IntPtr, int> _close;
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int> _enumerateDevices;
    private delegate* unmanaged[Cdecl]<IntPtr, CtlDeviceAdapterProperties*, int> _getDeviceProperties;
    private delegate* unmanaged[Cdecl]<IntPtr, CtlPowerTelemetry*, int> _powerTelemetryGet;

    // Optional refinements: a driver that rejects one of these still reports the telemetry snapshot.
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int> _enumMemoryModules;
    private delegate* unmanaged[Cdecl]<IntPtr, CtlMemState*, int> _memoryGetState;
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int> _enumFrequencyDomains;
    private delegate* unmanaged[Cdecl]<IntPtr, CtlFreqProperties*, int> _frequencyGetProperties;
    private delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int> _enumTemperatureSensors;
    private delegate* unmanaged[Cdecl]<IntPtr, CtlTempProperties*, int> _temperatureGetProperties;
    private delegate* unmanaged[Cdecl]<IntPtr, double*, int> _temperatureGetState;

    private IgclLibrary(ILogger logger, IntPtr library)
    {
        _logger = logger;
        _library = library;
    }

    /// <summary>
    /// Loads ControlLib.dll, binds the entry points, and initialises the API — or returns null when any of
    /// that is not possible on this machine.
    /// </summary>
    public static IgclLibrary? TryLoad(ILogger logger)
    {
        // The struct layouts are this binding's single real hazard, so they are proven against the header's
        // arithmetic before the library is allowed to see any of them.
        if (!LayoutsAreAsTranscribed(logger))
        {
            return null;
        }

        foreach (var candidate in WindowsCandidates)
        {
            if (!NativeLibrary.TryLoad(candidate, out var handle))
            {
                continue;
            }

            var library = new IgclLibrary(logger, handle);
            if (library.Bind() && library.Initialize())
            {
                return library;
            }

            library.Dispose();
            return null;
        }

        logger.LogDebug("ControlLib.dll not found; Intel GPU telemetry via IGCL is unavailable.");
        return null;
    }

    private bool Bind()
    {
        // Required — a ControlLib without these is one whose ABI we do not understand.
        if (!TryGet("ctlInit", out var init)
            || !TryGet("ctlClose", out var close)
            || !TryGet("ctlEnumerateDevices", out var enumerateDevices)
            || !TryGet("ctlGetDeviceProperties", out var getDeviceProperties)
            || !TryGet("ctlPowerTelemetryGet", out var powerTelemetryGet))
        {
            _logger.LogWarning("ControlLib.dll is present but missing required exports; Intel GPU telemetry stays off.");
            return false;
        }

        _init = (delegate* unmanaged[Cdecl]<CtlInitArgs*, IntPtr*, int>)init;
        _close = (delegate* unmanaged[Cdecl]<IntPtr, int>)close;
        _enumerateDevices = (delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int>)enumerateDevices;
        _getDeviceProperties = (delegate* unmanaged[Cdecl]<IntPtr, CtlDeviceAdapterProperties*, int>)getDeviceProperties;
        _powerTelemetryGet = (delegate* unmanaged[Cdecl]<IntPtr, CtlPowerTelemetry*, int>)powerTelemetryGet;

        if (TryGet("ctlEnumMemoryModules", out var enumMemory))
        {
            _enumMemoryModules = (delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int>)enumMemory;
        }

        if (TryGet("ctlMemoryGetState", out var memoryState))
        {
            _memoryGetState = (delegate* unmanaged[Cdecl]<IntPtr, CtlMemState*, int>)memoryState;
        }

        if (TryGet("ctlEnumFrequencyDomains", out var enumFrequency))
        {
            _enumFrequencyDomains = (delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int>)enumFrequency;
        }

        if (TryGet("ctlFrequencyGetProperties", out var frequencyProperties))
        {
            _frequencyGetProperties = (delegate* unmanaged[Cdecl]<IntPtr, CtlFreqProperties*, int>)frequencyProperties;
        }

        if (TryGet("ctlEnumTemperatureSensors", out var enumTemperature))
        {
            _enumTemperatureSensors = (delegate* unmanaged[Cdecl]<IntPtr, uint*, IntPtr*, int>)enumTemperature;
        }

        if (TryGet("ctlTemperatureGetProperties", out var temperatureProperties))
        {
            _temperatureGetProperties = (delegate* unmanaged[Cdecl]<IntPtr, CtlTempProperties*, int>)temperatureProperties;
        }

        if (TryGet("ctlTemperatureGetState", out var temperatureState))
        {
            _temperatureGetState = (delegate* unmanaged[Cdecl]<IntPtr, double*, int>)temperatureState;
        }

        return true;
    }

    private bool TryGet(string name, out IntPtr export) => NativeLibrary.TryGetExport(_library, name, out export);

    private bool Initialize()
    {
        var args = new CtlInitArgs
        {
            Size = (uint)Unsafe.SizeOf<CtlInitArgs>(),
            Version = 0,
            AppVersion = AppVersion,
            Flags = InitFlagUseLevelZero,
        };

        IntPtr apiHandle;
        var result = _init(&args, &apiHandle);
        if (result != CtlResultSuccess)
        {
            _logger.LogDebug("ctlInit failed with 0x{Result:X8}; Intel GPU telemetry is unavailable.", result);
            return false;
        }

        _apiHandle = apiHandle;
        return true;
    }

    /// <summary>Every Intel graphics adapter IGCL reports, with the identity fields needed to join it to PDH.</summary>
    public IReadOnlyList<IgclDevice> EnumerateDevices()
    {
        uint count = 0;
        if (_enumerateDevices(_apiHandle, &count, null) != CtlResultSuccess || count is 0 or > MaximumHandles)
        {
            return [];
        }

        var handles = stackalloc IntPtr[(int)count];
        if (_enumerateDevices(_apiHandle, &count, handles) != CtlResultSuccess)
        {
            return [];
        }

        List<IgclDevice> devices = [];
        for (var index = 0; index < count; index++)
        {
            var device = DescribeDevice(handles[index]);
            if (device is not null)
            {
                devices.Add(device);
            }
        }

        return devices;
    }

    private IgclDevice? DescribeDevice(IntPtr handle)
    {
        // pDeviceID is caller-allocated; on Windows the driver writes an adapter LUID (8 bytes) into it.
        var osDeviceId = stackalloc byte[8];
        var properties = new CtlDeviceAdapterProperties
        {
            Size = (uint)Unsafe.SizeOf<CtlDeviceAdapterProperties>(),
            // Version 2 asks for adapter_bdf, the field that joins this device to its PnP identity.
            Version = 2,
            DeviceId = (IntPtr)osDeviceId,
            DeviceIdSize = 8,
        };

        if (_getDeviceProperties(handle, &properties) != CtlResultSuccess)
        {
            return null;
        }

        // CTL_DEVICE_TYPE_GRAPHICS = 1; anything else is not a GPU.
        if (properties.DeviceType != 1)
        {
            return null;
        }

        // properties is a local, so its fixed-size buffer is already addressable without a fixed statement.
        var nameBytes = properties.Name;
        var nameLength = 0;
        while (nameLength < CtlDeviceAdapterProperties.NameLength && nameBytes[nameLength] != 0)
        {
            nameLength++;
        }

        var name = nameLength > 0 ? Encoding.UTF8.GetString(nameBytes, nameLength) : null;

        // BDF arrived only if the driver honoured Version 2; a zeroed BDF on bus 0 device 0 function 0 is
        // also what an integrated GPU legitimately reports as 00:02.0 — the device number disambiguates.
        var pciAddress = WindowsPciAddress.Format(
            properties.AdapterBdfBus,
            ((uint)properties.AdapterBdfDevice << 16) | properties.AdapterBdfFunction);

        return new IgclDevice
        {
            Handle = handle,
            Name = name,
            PciAddress = pciAddress,
            PciVendorId = properties.PciVendorId,
            PciDeviceId = properties.PciDeviceId,
        };
    }

    /// <summary>
    /// One telemetry snapshot for a device, or null when the call failed — including
    /// <c>CTL_RESULT_ERROR_DEVICE_UNAVAILABLE</c>, which is what a discrete Arc card in D3 answers. IGCL
    /// reports the low-power state as an error instead of waking the card, so unlike NVML there is no wake
    /// hazard to engineer around here.
    /// </summary>
    public IgclTelemetrySnapshot? TryGetTelemetry(IntPtr deviceHandle)
    {
        var telemetry = new CtlPowerTelemetry
        {
            Size = (uint)Unsafe.SizeOf<CtlPowerTelemetry>(),
            Version = 0,
        };

        if (_powerTelemetryGet(deviceHandle, &telemetry) != CtlResultSuccess)
        {
            return null;
        }

        return new IgclTelemetrySnapshot
        {
            TimestampSeconds = Decode(telemetry.TimeStamp),
            GpuEnergyJoules = Decode(telemetry.GpuEnergyCounter),
            TotalCardEnergyJoules = Decode(telemetry.TotalCardEnergyCounter),
            GlobalActivitySeconds = Decode(telemetry.GlobalActivityCounter),
            GpuTemperatureCelsius = Decode(telemetry.GpuCurrentTemperature),
            VramTemperatureCelsius = Decode(telemetry.VramCurrentTemperature),
            CoreClockMegahertz = Decode(telemetry.GpuCurrentClockFrequency),
            PowerLimited = telemetry.GpuPowerLimited != 0,
            TemperatureLimited = telemetry.GpuTemperatureLimited != 0,
            CurrentLimited = telemetry.GpuCurrentLimited != 0,
            VoltageLimited = telemetry.GpuVoltageLimited != 0,
            UtilizationLimited = telemetry.GpuUtilizationLimited != 0,
        };
    }

    /// <summary>Video memory (used, total) in bytes, from the device's first memory module.</summary>
    public (double? UsedBytes, double? TotalBytes) TryGetMemory(IntPtr deviceHandle)
    {
        if (_enumMemoryModules is null || _memoryGetState is null)
        {
            return (null, null);
        }

        uint count = 0;
        if (_enumMemoryModules(deviceHandle, &count, null) != CtlResultSuccess || count is 0 or > MaximumHandles)
        {
            return (null, null);
        }

        var handles = stackalloc IntPtr[(int)count];
        if (_enumMemoryModules(deviceHandle, &count, handles) != CtlResultSuccess || count == 0)
        {
            return (null, null);
        }

        var state = new CtlMemState
        {
            Size = (uint)Unsafe.SizeOf<CtlMemState>(),
            Version = 0,
        };

        if (_memoryGetState(handles[0], &state) != CtlResultSuccess || state.SizeBytes == 0)
        {
            return (null, null);
        }

        var used = state.SizeBytes >= state.FreeBytes ? state.SizeBytes - state.FreeBytes : 0;
        return (used, state.SizeBytes);
    }

    /// <summary>
    /// Reads the dedicated temperature sensors: the GPU sensor and the device-wide maximum.
    /// </summary>
    /// <remarks>
    /// A SEPARATE driver path from <c>ctlPowerTelemetryGet</c>'s <c>gpuCurrentTemperature</c>, which is why
    /// it is worth having: a part whose telemetry struct marks that field unsupported may still answer here.
    /// <c>CTL_TEMP_SENSORS_GLOBAL</c> is "the maximum temperature across all device sensors" — the same
    /// hotspot notion the neutral model already carries for amdgpu's junction sensor. The <c>_MIN</c> sensor
    /// variants are skipped: the coolest point on a die says nothing about whether it needs more airflow.
    /// </remarks>
    public (double? GpuCelsius, double? HottestCelsius) TryGetTemperatures(IntPtr deviceHandle)
    {
        if (_enumTemperatureSensors is null || _temperatureGetProperties is null || _temperatureGetState is null)
        {
            return (null, null);
        }

        uint count = 0;
        if (_enumTemperatureSensors(deviceHandle, &count, null) != CtlResultSuccess || count is 0 or > MaximumHandles)
        {
            return (null, null);
        }

        var handles = stackalloc IntPtr[(int)count];
        if (_enumTemperatureSensors(deviceHandle, &count, handles) != CtlResultSuccess)
        {
            return (null, null);
        }

        double? gpu = null;
        double? hottest = null;

        for (var index = 0; index < count; index++)
        {
            var properties = new CtlTempProperties
            {
                Size = (uint)Unsafe.SizeOf<CtlTempProperties>(),
                Version = 0,
            };

            if (_temperatureGetProperties(handles[index], &properties) != CtlResultSuccess)
            {
                continue;
            }

            if (properties.SensorType is not (TempSensorGpu or TempSensorGlobal))
            {
                continue;
            }

            double celsius;
            if (_temperatureGetState(handles[index], &celsius) != CtlResultSuccess || !double.IsFinite(celsius))
            {
                continue;
            }

            if (properties.SensorType == TempSensorGpu)
            {
                gpu = celsius;
            }
            else
            {
                hottest = celsius;
            }
        }

        return (gpu, hottest);
    }

    /// <summary>The GPU frequency domain's rated maximum clock in MHz, or null when it cannot be read.</summary>
    public double? TryGetMaxClockMegahertz(IntPtr deviceHandle)
    {
        if (_enumFrequencyDomains is null || _frequencyGetProperties is null)
        {
            return null;
        }

        uint count = 0;
        if (_enumFrequencyDomains(deviceHandle, &count, null) != CtlResultSuccess || count is 0 or > MaximumHandles)
        {
            return null;
        }

        var handles = stackalloc IntPtr[(int)count];
        if (_enumFrequencyDomains(deviceHandle, &count, handles) != CtlResultSuccess)
        {
            return null;
        }

        for (var index = 0; index < count; index++)
        {
            var properties = new CtlFreqProperties
            {
                Size = (uint)Unsafe.SizeOf<CtlFreqProperties>(),
                Version = 0,
            };

            if (_frequencyGetProperties(handles[index], &properties) != CtlResultSuccess)
            {
                continue;
            }

            if (properties.Domain == FreqDomainGpu && properties.Max > 0d)
            {
                return properties.Max;
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes one telemetry item to a double, honouring its declared data type; null when the device does
    /// not support that item or the value cannot be believed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intel's own samples (Overclocking_Sample, Telemetry_Samples) read <c>value.datadouble</c>
    /// unconditionally for every field consumed here, and <c>value.datau64</c> only for the two VRAM
    /// bandwidth counters — which this app does not read. So in practice these items are doubles.
    /// </para>
    /// <para>
    /// The type tag is honoured anyway, because that per-field split in the samples is only defensible if
    /// the tag is meaningful, and honouring it is correct under both readings. The finite check is the
    /// backstop for the one case where the two approaches diverge: a driver that tagged a field as an
    /// integer while writing a double would decode here as an absurd integer, which is caught below rather
    /// than published as a measurement.
    /// </para>
    /// </remarks>
    private static double? Decode(in CtlTelemetryItem item)
    {
        if (item.Supported == 0)
        {
            return null;
        }

        // ctl_data_type_t: INT8..UINT64 are 0..7, FLOAT 8, DOUBLE 9.
        double? value = item.Type switch
        {
            0 => (sbyte)item.ValueRaw,
            1 => (byte)item.ValueRaw,
            2 => (short)item.ValueRaw,
            3 => (ushort)item.ValueRaw,
            4 => (int)item.ValueRaw,
            5 => (uint)item.ValueRaw,
            6 => (long)item.ValueRaw,
            7 => item.ValueRaw,
            8 => BitConverter.Int32BitsToSingle((int)item.ValueRaw),
            9 => BitConverter.Int64BitsToDouble((long)item.ValueRaw),
            _ => null,
        };

        // NaN and infinity reach here from a float/double field the driver left uninitialised; neither is a
        // reading, and both would poison the counter deltas they feed.
        return value is { } number && double.IsFinite(number) ? number : null;
    }

    /// <summary>
    /// The sizes the C# structs above actually compile to, for comparison against
    /// <see cref="IgclStructLayout.Header"/>.
    /// </summary>
    /// <remarks>
    /// Public so the layout agreement is a TEST rather than only a runtime log line: the header numbers were
    /// obtained by compiling <c>igcl_api.h</c>'s field arithmetic with a C compiler, and a future edit that
    /// silently changes a field's type would otherwise only surface on a machine with an Intel GPU.
    /// </remarks>
    public static IgclStructLayout MeasuredLayout => new(
        TelemetryItem: Unsafe.SizeOf<CtlTelemetryItem>(),
        PsuInfo: Unsafe.SizeOf<CtlPsuInfo>(),
        PowerTelemetry: Unsafe.SizeOf<CtlPowerTelemetry>(),
        DeviceAdapterProperties: Unsafe.SizeOf<CtlDeviceAdapterProperties>(),
        InitArgs: Unsafe.SizeOf<CtlInitArgs>(),
        MemoryState: Unsafe.SizeOf<CtlMemState>(),
        FrequencyProperties: Unsafe.SizeOf<CtlFreqProperties>(),
        TemperatureProperties: Unsafe.SizeOf<CtlTempProperties>());

    /// <summary>
    /// Verifies the transcribed struct layouts against the sizes the header's own field arithmetic produces.
    /// A mismatch means this file no longer agrees with <c>igcl_api.h</c>, and the only safe response is to
    /// not call IGCL at all.
    /// </summary>
    private static bool LayoutsAreAsTranscribed(ILogger logger)
    {
        var measured = MeasuredLayout;
        if (measured == IgclStructLayout.Header)
        {
            return true;
        }

        logger.LogError(
            "IGCL struct layout mismatch: measured {Measured}, header {Header}. Intel GPU telemetry stays off "
            + "rather than calling with an ABI this build does not agree on.",
            measured,
            IgclStructLayout.Header);
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_apiHandle != IntPtr.Zero && _close is not null)
        {
            try
            {
                _close(_apiHandle);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "ctlClose failed during shutdown; continuing.");
            }

            _apiHandle = IntPtr.Zero;
        }

        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    // ----- Structs transcribed from igcl_api.h. Layout rules are MSVC x64: each field at its natural
    // ----- alignment, C 'bool' is one byte (mapped to byte here so the layout stays blittable).

    /// <summary>ctl_init_args_t: Size, Version, AppVersion, flags, SupportedVersion, ApplicationUID (16-byte GUID-alike).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlInitArgs
    {
        public uint Size;
        public byte Version;
        public uint AppVersion;
        public uint Flags;
        public uint SupportedVersion;

        // ctl_application_id_t: {u32, u16, u16, u8[8]}. The tail is two u32 fields rather than one u64 so it
        // stays 4-aligned at offset 28, exactly as the C layout places the byte array.
        public uint Uid1;
        public ushort Uid2;
        public ushort Uid3;
        public uint Uid4Low;
        public uint Uid4High;
    }

    /// <summary>ctl_oc_telemetry_item_t: bool bSupported; ctl_units_t units; ctl_data_type_t type; 8-byte value union.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CtlTelemetryItem
    {
        public byte Supported;
        public uint Units;
        public uint Type;

        /// <summary>The ctl_data_value_t union, held raw; <see cref="Decode"/> reinterprets it per Type.</summary>
        public ulong ValueRaw;
    }

    /// <summary>ctl_psu_info_t: bool bSupported; ctl_psu_type_t psuType; two telemetry items.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlPsuInfo
    {
        public byte Supported;
        public uint PsuType;
        public CtlTelemetryItem EnergyCounter;
        public CtlTelemetryItem Voltage;
    }

    /// <summary>
    /// ctl_power_telemetry_t, base (Version 0) layout. The Version-1+ tail fields are declared so Size
    /// matches the header, but only base fields are read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlPowerTelemetry
    {
        public uint Size;
        public byte Version;
        public CtlTelemetryItem TimeStamp;
        public CtlTelemetryItem GpuEnergyCounter;
        public CtlTelemetryItem GpuVoltage;
        public CtlTelemetryItem GpuCurrentClockFrequency;
        public CtlTelemetryItem GpuCurrentTemperature;
        public CtlTelemetryItem GlobalActivityCounter;
        public CtlTelemetryItem RenderComputeActivityCounter;
        public CtlTelemetryItem MediaActivityCounter;
        public byte GpuPowerLimited;
        public byte GpuTemperatureLimited;
        public byte GpuCurrentLimited;
        public byte GpuVoltageLimited;
        public byte GpuUtilizationLimited;
        public CtlTelemetryItem VramEnergyCounter;
        public CtlTelemetryItem VramVoltage;
        public CtlTelemetryItem VramCurrentClockFrequency;
        public CtlTelemetryItem VramCurrentEffectiveFrequency;
        public CtlTelemetryItem VramReadBandwidthCounter;
        public CtlTelemetryItem VramWriteBandwidthCounter;
        public CtlTelemetryItem VramCurrentTemperature;
        public byte VramPowerLimited;
        public byte VramTemperatureLimited;
        public byte VramCurrentLimited;
        public byte VramVoltageLimited;
        public byte VramUtilizationLimited;
        public CtlTelemetryItem TotalCardEnergyCounter;

        // ctl_psu_info_t psu[CTL_PSU_COUNT = 5]
        public CtlPsuInfo Psu0;
        public CtlPsuInfo Psu1;
        public CtlPsuInfo Psu2;
        public CtlPsuInfo Psu3;
        public CtlPsuInfo Psu4;

        // ctl_oc_telemetry_item_t fanSpeed[CTL_FAN_COUNT = 5]
        public CtlTelemetryItem Fan0;
        public CtlTelemetryItem Fan1;
        public CtlTelemetryItem Fan2;
        public CtlTelemetryItem Fan3;
        public CtlTelemetryItem Fan4;

        // Version > 0 tail — declared for Size only.
        public CtlTelemetryItem GpuVrTemp;
        public CtlTelemetryItem VramVrTemp;
        public CtlTelemetryItem SaVrTemp;
        public CtlTelemetryItem GpuEffectiveClock;
        public CtlTelemetryItem GpuOverVoltagePercent;
        public CtlTelemetryItem GpuPowerPercent;
        public CtlTelemetryItem GpuTemperaturePercent;
        public CtlTelemetryItem VramReadBandwidth;
        public CtlTelemetryItem VramWriteBandwidth;
    }

    /// <summary>ctl_device_adapter_properties_t at Version 2 (adds pci_subsys/BDF over the base layout).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct CtlDeviceAdapterProperties
    {
        public const int NameLength = 100;   // CTL_MAX_DEVICE_NAME_LEN
        public const int ReservedLength = 108; // CTL_MAX_RESERVED_SIZE

        public uint Size;
        public byte Version;
        public IntPtr DeviceId;
        public uint DeviceIdSize;
        public uint DeviceType;
        public uint SupportedSubfunctionFlags;
        public ulong DriverVersion;
        public ulong FirmwareMajor;
        public ulong FirmwareMinor;
        public ulong FirmwareBuild;
        public uint PciVendorId;
        public uint PciDeviceId;
        public uint RevId;
        public uint NumEusPerSubSlice;
        public uint NumSubSlicesPerSlice;
        public uint NumSlices;
        public fixed byte Name[NameLength];
        public uint GraphicsAdapterProperties;
        public uint Frequency;
        public ushort PciSubsysId;
        public ushort PciSubsysVendorId;
        public byte AdapterBdfBus;
        public byte AdapterBdfDevice;
        public byte AdapterBdfFunction;
        public uint NumXeCores;
        public fixed byte Reserved[ReservedLength];
    }

    /// <summary>ctl_mem_state_t: free and total allocatable bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlMemState
    {
        public uint Size;
        public byte Version;
        public ulong FreeBytes;
        public ulong SizeBytes;
    }

    /// <summary>ctl_temp_properties_t: which part the sensor measures, and that part's maximum.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlTempProperties
    {
        public uint Size;
        public byte Version;
        public uint SensorType;
        public double MaxTemperature;
    }

    /// <summary>ctl_freq_properties_t: domain type, controllability, hardware min/max MHz.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CtlFreqProperties
    {
        public uint Size;
        public byte Version;
        public uint Domain;
        public byte CanControl;
        public double Min;
        public double Max;
    }
}

/// <summary>
/// The byte sizes of the IGCL structs this app marshals, so the transcription can be checked against the
/// header instead of trusted.
/// </summary>
/// <param name="TelemetryItem"><c>ctl_oc_telemetry_item_t</c>.</param>
/// <param name="PsuInfo"><c>ctl_psu_info_t</c>.</param>
/// <param name="PowerTelemetry"><c>ctl_power_telemetry_t</c>.</param>
/// <param name="DeviceAdapterProperties"><c>ctl_device_adapter_properties_t</c>.</param>
/// <param name="InitArgs"><c>ctl_init_args_t</c>.</param>
/// <param name="MemoryState"><c>ctl_mem_state_t</c>.</param>
/// <param name="FrequencyProperties"><c>ctl_freq_properties_t</c>.</param>
/// <param name="TemperatureProperties"><c>ctl_temp_properties_t</c>.</param>
public readonly record struct IgclStructLayout(
    int TelemetryItem,
    int PsuInfo,
    int PowerTelemetry,
    int DeviceAdapterProperties,
    int InitArgs,
    int MemoryState,
    int FrequencyProperties,
    int TemperatureProperties)
{
    /// <summary>
    /// What <c>igcl_api.h</c> produces on the MSVC x64 ABI IGCL ships for.
    /// </summary>
    /// <remarks>
    /// Not guessed: obtained by compiling the header's struct definitions verbatim and printing
    /// <c>sizeof</c> for each. Update these ONLY alongside a matching re-measurement.
    /// </remarks>
    public static IgclStructLayout Header { get; } = new(
        TelemetryItem: 24,
        PsuInfo: 56,
        PowerTelemetry: 1024,
        DeviceAdapterProperties: 320,
        InitArgs: 36,
        MemoryState: 24,
        FrequencyProperties: 32,
        TemperatureProperties: 24);
}

/// <summary>One Intel graphics adapter as IGCL describes it.</summary>
public sealed record IgclDevice
{
    public required IntPtr Handle { get; init; }

    public string? Name { get; init; }

    /// <summary>Canonical PCI address from the adapter BDF, the join key to the PnP identity.</summary>
    public string? PciAddress { get; init; }

    public uint PciVendorId { get; init; }

    public uint PciDeviceId { get; init; }
}

/// <summary>The decoded, unit-normalised base fields of one ctlPowerTelemetryGet snapshot.</summary>
public sealed record IgclTelemetrySnapshot
{
    /// <summary>The snapshot's own timestamp in seconds — the clock the energy/activity counters share.</summary>
    public double? TimestampSeconds { get; init; }

    /// <summary>Monotonic GPU-chip energy counter, joules. Power is the delta over time.</summary>
    public double? GpuEnergyJoules { get; init; }

    /// <summary>Monotonic whole-card energy counter, joules — the board figure, preferred where present.</summary>
    public double? TotalCardEnergyJoules { get; init; }

    /// <summary>Monotonic busy-time counter, seconds. Utilisation is the delta over elapsed time.</summary>
    public double? GlobalActivitySeconds { get; init; }

    public double? GpuTemperatureCelsius { get; init; }

    public double? VramTemperatureCelsius { get; init; }

    public double? CoreClockMegahertz { get; init; }

    public bool PowerLimited { get; init; }

    public bool TemperatureLimited { get; init; }

    public bool CurrentLimited { get; init; }

    public bool VoltageLimited { get; init; }

    public bool UtilizationLimited { get; init; }
}
