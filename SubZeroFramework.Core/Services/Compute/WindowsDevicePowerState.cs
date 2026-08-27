// Compiled only into the windows TFM of Core, like the other SetupAPI callers here.
#if WINDOWS10_0_26100_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Vanara.PInvoke;

using static Vanara.PInvoke.SetupAPI;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Asks Windows whether a device is powered up, so a sleeping GPU can be left alone.
/// </summary>
/// <remarks>
/// <para>
/// This is the Windows counterpart of the Linux readers' <c>power/runtime_status</c> gate, and it exists for
/// the same measured reason: a vendor-SDK call against a suspended laptop dGPU does not merely fail, it WAKES
/// the device. On the reference machine an NVML call to an awake GPU returns in 0.02 ms while one that has to
/// wake it takes 480-600 ms and takes the board from ~17.9 W to ~29 W. Polling therefore costs roughly 19 W
/// for telemetry nobody asked for.
/// </para>
/// <para>
/// <b>Vendor-neutral by construction.</b> It reads the OS device power state by device instance ID and never
/// touches NVML, ADLX or IGCL — so the same gate serves an NVIDIA, AMD or Intel reader, and a machine with a
/// vendor SDK this app has never heard of still gets the protection.
/// </para>
/// <para>
/// It replaces a guess with a fact. Inferring sleep from how long the last call took works, but a call that
/// is slow for any other reason then looks like a sleeping GPU — and reporting a busy GPU as idle is the one
/// error an adaptive fan cannot recover from quickly.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsDevicePowerState
{
    /// <summary>
    /// <c>DEVPKEY_Device_PowerState</c>, whose value is a <c>DEVICE_POWER_STATE</c>.
    /// </summary>
    /// <remarks>
    /// Spelled out in the same DEFINE_DEVPROPKEY form the Windows headers use, matching how the identity
    /// resolver in this folder declares the keys Vanara does not name.
    /// </remarks>
    private static readonly DEVPROPKEY PowerStateKey =
        new(0x80497100, 0x8c73, 0x48b9, 0xaa, 0xd9, 0xce, 0x38, 0x7e, 0x19, 0xc5, 0x6e, 2);

    /// <summary><c>PowerDeviceD0</c> — the only state in which the device is fully up.</summary>
    private const uint PowerDeviceD0 = 1;

    /// <summary>
    /// True when the device is in D0, false when it is in a low-power state, null when unknown.
    /// </summary>
    /// <remarks>
    /// <b>Null is not "asleep".</b> A device whose power state cannot be read must be sampled normally, or a
    /// machine this lookup does not understand would silently report every GPU as idle forever. Only a
    /// definite low-power answer suppresses a read.
    /// </remarks>
    public static bool? IsAwake(string? deviceInstanceId)
    {
        if (string.IsNullOrWhiteSpace(deviceInstanceId))
        {
            return null;
        }

        try
        {
            // An empty set with one device opened into it, rather than enumerating a class and searching:
            // this runs on the telemetry path and the instance ID names the device exactly.
            using var deviceInfoSet = SetupDiCreateDeviceInfoList();
            if (deviceInfoSet.IsInvalid)
            {
                return null;
            }

            var deviceInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            if (!SetupDiOpenDeviceInfo(deviceInfoSet, deviceInstanceId, HWND.NULL, 0, ref deviceInfo))
            {
                return null;
            }

            return TryReadPowerState(deviceInfoSet, in deviceInfo) is { } state ? state == PowerDeviceD0 : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // A Windows install that cannot serve this is one that samples normally, not one that crashes.
            return null;
        }
    }

    private static unsafe uint? TryReadPowerState(HDEVINFO deviceInfoSet, in SP_DEVINFO_DATA deviceInfo)
    {
        uint value;
        if (!SetupDiGetDeviceProperty(deviceInfoSet, in deviceInfo, in PowerStateKey, out DEVPROPTYPE propertyType, (IntPtr)(&value), sizeof(uint), out _)
            || propertyType != DEVPROPTYPE.DEVPROP_TYPE_UINT32)
        {
            return null;
        }

        return value;
    }
}
#endif
