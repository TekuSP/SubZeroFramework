using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

namespace SubZeroFramework.Services.Linux;

/// <summary>The mode a connector is actually scanning out right now.</summary>
public sealed record DrmActiveMode
{
    /// <summary>Connector label in the kernel's own form, e.g. "eDP-1" — matches the sysfs directory name.</summary>
    public required string ConnectorName { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Vertical refresh in hertz, computed the way the kernel's own drm_mode_vrefresh does.</summary>
    public required double RefreshHz { get; init; }

    /// <summary>Mode name as the driver reports it, e.g. "2560x1600".</summary>
    public string? ModeName { get; init; }
}

/// <summary>
/// Reads the ACTIVE display mode from the kernel's DRM interface, with no display server.
/// </summary>
/// <remarks>
/// sysfs answers what a panel can do, not what it is doing: the EDID's preferred timing is frequently 60 Hz on
/// a panel currently running at 165 Hz, because the high-refresh modes live in an extension block and the
/// active mode is chosen by whoever set it. The current mode exists only in the kernel's mode-setting state,
/// which is reachable through four read-only ioctls on <c>/dev/dri/cardN</c>.
///
/// This is READ-ONLY and takes no DRM master: the GET ioctls are defined with no permission flags, so opening
/// the node alongside a running compositor is safe and changes nothing. Nothing here ever sets a mode.
///
/// The connector query deliberately passes a non-zero mode count. Asking for zero modes is the documented
/// trigger for a full connector re-probe (the expensive DDC path <c>xrandr -q</c> takes), which is exactly
/// what a background service must not do to a running display every refresh.
/// </remarks>
public static partial class DrmModeReader
{
    // ioctl request numbers, _IOWR('d', nr, struct). Encoding: (dir<<30)|(size<<16)|(type<<8)|nr, with
    // dir=3 for read/write and type='d'=0x64. The sizes below are the UAPI struct sizes asserted in the
    // static constructor, so a layout mistake fails loudly at startup instead of corrupting memory.
    private const uint DrmIoctlVersion = 0xC0406400;
    private const uint DrmIoctlModeGetResources = 0xC04064A0;
    private const uint DrmIoctlModeGetCrtc = 0xC06864A1;
    private const uint DrmIoctlModeGetEncoder = 0xC01464A6;
    private const uint DrmIoctlModeGetConnector = 0xC05064A7;

    private const int OpenReadOnly = 0;
    private const int OpenCloseOnExec = 0x80000;

    private const uint DrmModeConnected = 1;

    private const uint DrmModeFlagInterlace = 0x10;
    private const uint DrmModeFlagDoubleScan = 0x20;

    static DrmModeReader()
    {
        // The ioctl number encodes sizeof(struct); if these ever disagree the kernel would reject the call
        // (or worse, read the wrong length), so assert the agreement once rather than debug it in the field.
        Verify<DrmVersion>(DrmIoctlVersion);
        Verify<DrmModeCardRes>(DrmIoctlModeGetResources);
        Verify<DrmModeCrtc>(DrmIoctlModeGetCrtc);
        Verify<DrmModeGetEncoder>(DrmIoctlModeGetEncoder);
        Verify<DrmModeGetConnector>(DrmIoctlModeGetConnector);

        static void Verify<T>(uint request) where T : unmanaged
        {
            var encodedSize = (request >> 16) & 0x3FFF;
            var actualSize = Marshal.SizeOf<T>();
            if (encodedSize != actualSize)
            {
                throw new InvalidOperationException(
                    $"DRM ioctl 0x{request:X8} encodes a {encodedSize}-byte payload but {typeof(T).Name} marshals to {actualSize} bytes.");
            }
        }
    }

    /// <summary>
    /// Reads the active mode of every connected connector on one card, keyed by connector name ("eDP-1").
    /// </summary>
    /// <remarks>
    /// Returns empty for any reason at all — no device node, no permission, a driver without mode setting, a
    /// kernel that rejects an ioctl. Callers fall back to the EDID's preferred timing.
    /// </remarks>
    public static IReadOnlyDictionary<string, DrmActiveMode> ReadActiveModes(
        int cardIndex,
        ILogger? logger = null,
        string deviceRoot = "/dev/dri")
    {
        Dictionary<string, DrmActiveMode> modes = new(StringComparer.OrdinalIgnoreCase);
        var devicePath = Path.Combine(deviceRoot, $"card{cardIndex}");

        if (!File.Exists(devicePath))
        {
            return modes;
        }

        var fd = -1;
        try
        {
            fd = Open(devicePath, OpenReadOnly | OpenCloseOnExec);
            if (fd < 0)
            {
                // A container without /dev/dri passed through, or a hardened system. Not an error.
                logger?.LogDebug("Could not open {DevicePath}; active display modes will come from EDID instead.", devicePath);
                return modes;
            }

            foreach (var connectorId in ReadConnectorIds(fd))
            {
                var mode = ReadConnectorActiveMode(fd, connectorId);
                if (mode is not null)
                {
                    modes[mode.ConnectorName] = mode;
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            logger?.LogDebug(exception, "Reading DRM modes from {DevicePath} failed; falling back to EDID timings.", devicePath);
        }
        finally
        {
            if (fd >= 0)
            {
                _ = Close(fd);
            }
        }

        return modes;
    }

    private static unsafe uint[] ReadConnectorIds(int fd)
    {
        var resources = default(DrmModeCardRes);

        // First pass with every pointer null: the kernel fills in the counts only.
        if (Ioctl(fd, DrmIoctlModeGetResources, &resources) != 0 || resources.CountConnectors == 0)
        {
            return [];
        }

        var connectorIds = new uint[resources.CountConnectors];
        fixed (uint* connectorIdPtr = connectorIds)
        {
            var request = default(DrmModeCardRes);
            // Ask ONLY for connectors; leaving the other counts at zero stops the kernel writing those arrays.
            request.ConnectorIdPtr = (ulong)connectorIdPtr;
            request.CountConnectors = (uint)connectorIds.Length;

            if (Ioctl(fd, DrmIoctlModeGetResources, &request) != 0)
            {
                return [];
            }

            // A connector could have disappeared between the two calls (hot-unplug).
            var count = Math.Min(request.CountConnectors, (uint)connectorIds.Length);
            return count == connectorIds.Length ? connectorIds : connectorIds[..(int)count];
        }
    }

    private static unsafe DrmActiveMode? ReadConnectorActiveMode(int fd, uint connectorId)
    {
        var connector = default(DrmModeGetConnector);
        connector.ConnectorId = connectorId;

        // Non-zero count_modes with a real buffer: asking for zero modes makes the kernel re-probe the
        // connector, which is a DDC round trip on live hardware. One slot is enough — the modes themselves
        // are not wanted here, only the scalar fields alongside them.
        var modeScratch = default(DrmModeInfo);
        connector.ModesPtr = (ulong)&modeScratch;
        connector.CountModes = 1;

        if (Ioctl(fd, DrmIoctlModeGetConnector, &connector) != 0)
        {
            return null;
        }

        if (connector.Connection != DrmModeConnected || connector.EncoderId == 0)
        {
            // Not connected, or connected but not driven by any encoder — so nothing is being scanned out.
            return null;
        }

        var encoder = default(DrmModeGetEncoder);
        encoder.EncoderId = connector.EncoderId;
        if (Ioctl(fd, DrmIoctlModeGetEncoder, &encoder) != 0 || encoder.CrtcId == 0)
        {
            return null;
        }

        var crtc = default(DrmModeCrtc);
        crtc.CrtcId = encoder.CrtcId;
        if (Ioctl(fd, DrmIoctlModeGetCrtc, &crtc) != 0 || crtc.ModeValid == 0)
        {
            return null;
        }

        var mode = crtc.Mode;
        var name = GetConnectorName(connector.ConnectorType, connector.ConnectorTypeId);

        return new DrmActiveMode
        {
            ConnectorName = name,
            Width = mode.HDisplay,
            Height = mode.VDisplay,
            RefreshHz = ComputeRefreshHz(mode),
            ModeName = ReadModeName(mode),
        };
    }

    /// <summary>
    /// Vertical refresh from the mode timings, mirroring the kernel's own drm_mode_vrefresh().
    /// </summary>
    /// <remarks>
    /// The struct also carries a <c>vrefresh</c> field, but it is a rounded integer that some drivers leave
    /// at zero, so the timings are the reliable source.
    /// </remarks>
    private static double ComputeRefreshHz(DrmModeInfo mode)
    {
        if (mode.HTotal == 0 || mode.VTotal == 0)
        {
            return 0d;
        }

        // clock is in kHz.
        var numerator = mode.Clock * 1000d;
        var denominator = (double)mode.HTotal * mode.VTotal;

        if ((mode.Flags & DrmModeFlagInterlace) != 0)
        {
            // Two fields per frame, so the field rate is twice the frame rate.
            numerator *= 2d;
        }

        if ((mode.Flags & DrmModeFlagDoubleScan) != 0)
        {
            denominator *= 2d;
        }

        if (mode.VScan > 1)
        {
            denominator *= mode.VScan;
        }

        return denominator <= 0d ? 0d : numerator / denominator;
    }

    private static unsafe string? ReadModeName(DrmModeInfo mode)
    {
        var length = 0;
        while (length < 32 && mode.Name[length] != 0)
        {
            length++;
        }

        return length == 0 ? null : System.Text.Encoding.ASCII.GetString(mode.Name, length);
    }

    /// <summary>
    /// Builds the connector label the kernel itself uses for the sysfs directory, so the two can be joined.
    /// </summary>
    /// <remarks>
    /// Names come from the kernel's drm_connector_enum_list; they are UAPI in effect, because they are what
    /// appears in <c>/sys/class/drm/card0-eDP-1</c> and in every display tool's output.
    /// </remarks>
    public static string GetConnectorName(uint connectorType, uint connectorTypeId)
    {
        var typeName = connectorType switch
        {
            1 => "VGA",
            2 => "DVI-I",
            3 => "DVI-D",
            4 => "DVI-A",
            5 => "Composite",
            6 => "SVIDEO",
            7 => "LVDS",
            8 => "Component",
            9 => "DIN",
            10 => "DP",
            11 => "HDMI-A",
            12 => "HDMI-B",
            13 => "TV",
            14 => "eDP",
            15 => "Virtual",
            16 => "DSI",
            17 => "DPI",
            18 => "Writeback",
            19 => "SPI",
            20 => "USB",
            _ => "Unknown",
        };

        return $"{typeName}-{connectorTypeId}";
    }

    /// <summary>
    /// The driver's own version triple for one card, e.g. "1.6.0" for i915 — or null if unavailable.
    /// </summary>
    /// <remarks>
    /// This exists because <c>/sys/module/&lt;driver&gt;/version</c> only exists for out-of-tree modules (NVIDIA's
    /// DKMS build has one; i915, amdgpu and nouveau built in-tree do not), so the sysfs path reports nothing on
    /// most machines. The DRM VERSION ioctl answers for every driver.
    ///
    /// Only the version triple is read. The struct also carries name/date/desc strings, but they need a second
    /// call with caller-allocated buffers, and the one field worth having — <c>date</c> — is dead: upstream
    /// removed DRIVER_DATE, and current kernels return the literal string "0". The triple comes back on the
    /// first call with null string pointers, so this needs no buffer management at all.
    ///
    /// Read-only and takes no DRM master, exactly like the mode queries above.
    /// </remarks>
    public static string? ReadDriverVersion(int cardIndex, ILogger? logger = null, string deviceRoot = "/dev/dri")
    {
        var devicePath = Path.Combine(deviceRoot, $"card{cardIndex}");
        if (!File.Exists(devicePath))
        {
            return null;
        }

        var fd = -1;
        try
        {
            fd = Open(devicePath, OpenReadOnly | OpenCloseOnExec);
            if (fd < 0)
            {
                return null;
            }

            unsafe
            {
                var version = default(DrmVersion);
                if (Ioctl(fd, DrmIoctlVersion, &version) != 0)
                {
                    return null;
                }

                // A driver that reports 0.0.0 is telling us nothing; surface null so the UI says Unknown
                // rather than printing a meaningless triple.
                if (version.VersionMajor == 0 && version.VersionMinor == 0 && version.VersionPatchLevel == 0)
                {
                    return null;
                }

                return $"{version.VersionMajor}.{version.VersionMinor}.{version.VersionPatchLevel}";
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            logger?.LogDebug(exception, "Could not read the DRM driver version for card {CardIndex}.", cardIndex);
            return null;
        }
        finally
        {
            if (fd >= 0)
            {
                _ = Close(fd);
            }
        }
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fd);

    // ioctl is variadic in C; a single pointer argument passes identically under the SysV and AArch64 ABIs.
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int Ioctl(int fd, nuint request, void* argument);

    private static unsafe int Ioctl<T>(int fd, uint request, T* argument) where T : unmanaged =>
        Ioctl(fd, request, (void*)argument);

    // ----- UAPI structs (include/uapi/drm/drm_mode.h) -----

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeCardRes
    {
        public ulong FbIdPtr;
        public ulong CrtcIdPtr;
        public ulong ConnectorIdPtr;
        public ulong EncoderIdPtr;
        public uint CountFbs;
        public uint CountCrtcs;
        public uint CountConnectors;
        public uint CountEncoders;
        public uint MinWidth;
        public uint MaxWidth;
        public uint MinHeight;
        public uint MaxHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DrmModeInfo
    {
        public uint Clock;
        public ushort HDisplay;
        public ushort HSyncStart;
        public ushort HSyncEnd;
        public ushort HTotal;
        public ushort HSkew;
        public ushort VDisplay;
        public ushort VSyncStart;
        public ushort VSyncEnd;
        public ushort VTotal;
        public ushort VScan;
        public uint VRefresh;
        public uint Flags;
        public uint Type;
        public fixed byte Name[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeCrtc
    {
        public ulong SetConnectorsPtr;
        public uint CountConnectors;
        public uint CrtcId;
        public uint FbId;
        public uint X;
        public uint Y;
        public uint GammaSize;
        public uint ModeValid;
        public DrmModeInfo Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeGetEncoder
    {
        public uint EncoderId;
        public uint EncoderType;
        public uint CrtcId;
        public uint PossibleCrtcs;
        public uint PossibleClones;
    }

    /// <summary>
    /// UAPI <c>struct drm_version</c>. The three ints are followed by 4 bytes of padding before the first
    /// pointer-sized field on a 64-bit ABI; the static constructor asserts the resulting 64-byte size against
    /// the size encoded in the ioctl number, so a layout mistake fails at startup rather than in the kernel.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DrmVersion
    {
        public int VersionMajor;
        public int VersionMinor;
        public int VersionPatchLevel;
        public nuint NameLen;
        public ulong NamePtr;
        public nuint DateLen;
        public ulong DatePtr;
        public nuint DescLen;
        public ulong DescPtr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeGetConnector
    {
        public ulong EncodersPtr;
        public ulong ModesPtr;
        public ulong PropsPtr;
        public ulong PropValuesPtr;
        public uint CountModes;
        public uint CountProps;
        public uint CountEncoders;
        public uint EncoderId;
        public uint ConnectorId;
        public uint ConnectorType;
        public uint ConnectorTypeId;
        public uint Connection;
        public uint MmWidth;
        public uint MmHeight;
        public uint Subpixel;
        public uint Pad;
    }
}
