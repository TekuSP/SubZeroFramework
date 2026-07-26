// Compiled only into the windows TFM of Core - see WindowsPdhComputeUtilizationReader.cs.
#if WINDOWS10_0_26100_0_OR_GREATER
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

using Vanara.PInvoke;

using static Vanara.PInvoke.SetupAPI;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Names the machine's GPUs and NPUs by walking the Windows SetupAPI device-property store.
/// </summary>
/// <remarks>
/// SetupAPI rather than WMI because only the device property store carries <c>DEVPKEY_Gpu_Luid</c>, and the
/// LUID is the sole link between a device and its <c>GPU Engine</c> performance-counter instances. WMI can name
/// the devices but leaves them unmatched, so it would be a second mechanism that still needed this one.
/// <para>
/// Two device classes are walked because an NPU is not a display adapter: Windows enumerates it as an MCDM
/// compute-only device in the ComputeAccelerator class, while it still appears under <c>GPU Engine</c> as its
/// own adapter.
/// </para>
/// </remarks>
// Core is cross-platform and also runs on Linux, and every Vanara assembly is marked windows-only, so the
// platform is declared here rather than defended call by call: the service only ever constructs this type
// inside an OperatingSystem.IsWindows() branch, which is what the compiler now checks. Enumerate still returns
// an empty list off Windows, because this attribute is a compile-time contract and not a runtime one.
[SupportedOSPlatform("windows")]
public sealed class WindowsComputeDeviceIdentityResolver : IComputeDeviceIdentityResolver
{
    private static readonly Guid DisplayAdapterClass = GUID_DEVCLASS_DISPLAY;
    private static readonly Guid ComputeAcceleratorClass = GUID_DEVCLASS_COMPUTEACCELERATOR;

    // Vanara declares the well-known device property keys but not the display-adapter ones, so the two GPU keys
    // are spelled out in the same DEFINE_DEVPROPKEY form the Windows headers use:
    // {60B193CB-5276-4D0F-96FC-F173ABAD3EC6} PID 2 is Gpu_Luid (UINT64) and PID 3 is Gpu_PhyId (UINT32).
    private static readonly DEVPROPKEY GpuLuidKey = new(0x60b193cb, 0x5276, 0x4d0f, 0x96, 0xfc, 0xf1, 0x73, 0xab, 0xad, 0x3e, 0xc6, 2);
    private static readonly DEVPROPKEY GpuPhysicalIdKey = new(0x60b193cb, 0x5276, 0x4d0f, 0x96, 0xfc, 0xf1, 0x73, 0xab, 0xad, 0x3e, 0xc6, 3);
    private static readonly DEVPROPKEY NameKey = DEVPKEY_NAME;
    private static readonly DEVPROPKEY FriendlyNameKey = DEVPKEY_Device_FriendlyName;
    private static readonly DEVPROPKEY DeviceDescriptionKey = DEVPKEY_Device_DeviceDesc;

    // A device name or instance path that needs more than this is not a shape we understand; refusing the read
    // is cheaper than trusting a driver-supplied length.
    private const uint MaximumPropertyBytes = 8192;
    private const uint MaximumInstanceIdCharacters = 4096;

    private readonly ILogger<WindowsComputeDeviceIdentityResolver> _logger;

    private int _enumerationFailureLogged;

    public WindowsComputeDeviceIdentityResolver(ILogger<WindowsComputeDeviceIdentityResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ComputeDeviceIdentity> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        List<ComputeDeviceIdentity> devices = [];

        // Each class is collected independently so a failure on one still yields the other — a machine with
        // GPUs and no readable accelerator class must not lose its GPUs.
        AppendDevicesOfClass(in DisplayAdapterClass, ComputeDeviceKind.Gpu, devices);
        AppendDevicesOfClass(in ComputeAcceleratorClass, ComputeDeviceKind.Npu, devices);

        return devices;
    }

    private void AppendDevicesOfClass(in Guid classGuid, ComputeDeviceKind kind, List<ComputeDeviceIdentity> devices)
    {
        try
        {
            AppendDevicesOfClassCore(in classGuid, kind, devices);
        }
        catch (Exception exception)
        {
            LogEnumerationFailureOnce(exception, kind);
        }
    }

    private void AppendDevicesOfClassCore(in Guid classGuid, ComputeDeviceKind kind, List<ComputeDeviceIdentity> devices)
    {
        // SafeHDEVINFO disposes through SetupDiDestroyDeviceInfoList, so the set is released even if a property
        // read throws mid-enumeration.
        using SafeHDEVINFO deviceInfoSet = SetupDiGetClassDevs(in classGuid, null, HWND.NULL, DIGCF.DIGCF_PRESENT);
        if (deviceInfoSet.IsInvalid)
        {
            LogEnumerationFailureOnce(new Win32Exception(Marshal.GetLastPInvokeError()), kind);
            return;
        }

        // Vanara's enumerable overload runs the SetupDiEnumDeviceInfo index loop and sizes SP_DEVINFO_DATA for us.
        foreach (SP_DEVINFO_DATA deviceInfo in SetupDiEnumDeviceInfo(deviceInfoSet))
        {
            string? deviceKey = TryReadDeviceInstanceId(deviceInfoSet, in deviceInfo);
            if (deviceKey is null)
            {
                // Without an instance path there is no restart-safe key, and a LUID-keyed channel would break
                // on the next reboot — so the device is skipped rather than published under an unstable name.
                continue;
            }

            ulong? adapterLuid = TryReadUInt64Property(deviceInfoSet, in deviceInfo, in GpuLuidKey);
            uint? physicalAdapterIndex = TryReadUInt32Property(deviceInfoSet, in deviceInfo, in GpuPhysicalIdKey);

            devices.Add(new ComputeDeviceIdentity
            {
                DeviceKey = deviceKey,
                Kind = kind,
                DisplayName = ReadDisplayName(deviceInfoSet, in deviceInfo, kind),
                AdapterLuid = adapterLuid is { } luid ? unchecked((long)luid) : null,
                PhysicalAdapterIndex = physicalAdapterIndex is { } physicalIndex && physicalIndex <= int.MaxValue
                    ? (int)physicalIndex
                    : null,
            });
        }
    }

    private static string ReadDisplayName(HDEVINFO deviceInfoSet, in SP_DEVINFO_DATA deviceInfo, ComputeDeviceKind kind)
    {
        return TryReadStringProperty(deviceInfoSet, in deviceInfo, in NameKey)
            ?? TryReadStringProperty(deviceInfoSet, in deviceInfo, in FriendlyNameKey)
            ?? TryReadStringProperty(deviceInfoSet, in deviceInfo, in DeviceDescriptionKey)
            ?? (kind == ComputeDeviceKind.Npu ? "Compute accelerator" : "Graphics adapter");
    }

    private static string? TryReadDeviceInstanceId(HDEVINFO deviceInfoSet, in SP_DEVINFO_DATA deviceInfo)
    {
        // A null buffer is how the API reports the length it needs; Vanara's signature is non-nullable because
        // the parameter is only optional in this probing call.
        SetupDiGetDeviceInstanceId(deviceInfoSet, in deviceInfo, null!, 0, out uint requiredCharacters);
        if (requiredCharacters is 0 or > MaximumInstanceIdCharacters)
        {
            return null;
        }

        StringBuilder buffer = new((int)requiredCharacters);
        if (!SetupDiGetDeviceInstanceId(deviceInfoSet, in deviceInfo, buffer, requiredCharacters, out _))
        {
            return null;
        }

        return TrimAtTerminator(buffer.ToString());
    }

    private static unsafe string? TryReadStringProperty(HDEVINFO deviceInfoSet, in SP_DEVINFO_DATA deviceInfo, in DEVPROPKEY propertyKey)
    {
        SetupDiGetDeviceProperty(deviceInfoSet, in deviceInfo, in propertyKey, out DEVPROPTYPE propertyType, IntPtr.Zero, 0, out uint requiredBytes);
        if (propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING || requiredBytes < sizeof(char) || requiredBytes > MaximumPropertyBytes || (requiredBytes & 1) != 0)
        {
            return null;
        }

        byte[] buffer = new byte[requiredBytes];
        fixed (byte* pointer = buffer)
        {
            if (!SetupDiGetDeviceProperty(deviceInfoSet, in deviceInfo, in propertyKey, out _, (IntPtr)pointer, requiredBytes, out _))
            {
                return null;
            }
        }

        return TrimAtTerminator(MemoryMarshal.Cast<byte, char>(buffer));
    }

    private static unsafe ulong? TryReadUInt64Property(HDEVINFO deviceInfoSet, in SP_DEVINFO_DATA deviceInfo, in DEVPROPKEY propertyKey)
    {
        ulong value;
        if (!SetupDiGetDeviceProperty(deviceInfoSet, in deviceInfo, in propertyKey, out DEVPROPTYPE propertyType, (IntPtr)(&value), sizeof(ulong), out _)
            || propertyType != DEVPROPTYPE.DEVPROP_TYPE_UINT64)
        {
            return null;
        }

        return value;
    }

    private static unsafe uint? TryReadUInt32Property(HDEVINFO deviceInfoSet, in SP_DEVINFO_DATA deviceInfo, in DEVPROPKEY propertyKey)
    {
        uint value;
        if (!SetupDiGetDeviceProperty(deviceInfoSet, in deviceInfo, in propertyKey, out DEVPROPTYPE propertyType, (IntPtr)(&value), sizeof(uint), out _)
            || propertyType != DEVPROPTYPE.DEVPROP_TYPE_UINT32)
        {
            return null;
        }

        return value;
    }

    private static string? TrimAtTerminator(ReadOnlySpan<char> characters)
    {
        int terminator = characters.IndexOf('\0');
        ReadOnlySpan<char> text = terminator >= 0 ? characters[..terminator] : characters;
        return text.IsWhiteSpace() ? null : text.ToString();
    }

    private void LogEnumerationFailureOnce(Exception exception, ComputeDeviceKind kind)
    {
        // A broken or absent device class repeats on every slow-tier pass; one line is enough to diagnose it and
        // a line per pass would drown the service log.
        if (Interlocked.Exchange(ref _enumerationFailureLogged, 1) != 0)
        {
            return;
        }

        _logger.LogWarning(exception, "Unable to enumerate {DeviceKind} devices through SetupAPI. Utilization for devices of that kind will be unavailable (logged once).", kind);
    }
}
#endif
