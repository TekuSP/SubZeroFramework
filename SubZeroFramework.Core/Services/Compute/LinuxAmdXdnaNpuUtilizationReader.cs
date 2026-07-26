using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Reads AMD XDNA (Ryzen AI) NPU busyness through the amdxdna driver's sensor query.
/// </summary>
/// <remarks>
/// Unlike every other source here this one is an ioctl, because the driver exposes no busy attribute in
/// sysfs. <c>DRM_AMDXDNA_QUERY_SENSORS</c> returns one record per NPU column with an INSTANTANEOUS 0–100
/// percentage sourced from the platform metrics table, so no delta arithmetic is needed.
///
/// It is narrowly available, and all of these must hold or the query simply fails and this reader reports
/// nothing: the sensor path landed well after the driver itself (the driver merged in 6.14, column
/// utilization much later), the kernel must have AMD's platform-management driver built and bound, and the
/// metrics table is only wired up for Strix and Krackan parts. Every one of those is reported by the ioctl
/// returning an error rather than by anything we can probe cheaply up front, so the reader tries once and
/// latches off if it is not supported.
///
/// POWER — the important caveat, and the reason for the gate below. Reading these sensors DOES wake the NPU:
/// the driver's query handler takes a runtime-PM reference around the whole call. Polling it every second
/// would hold the NPU powered up permanently, which in a fan-control application is precisely backwards. So
/// the runtime power state is checked from sysfs first, and the ioctl is issued ONLY when the NPU is already
/// awake. A suspended NPU is reported as 0% busy — which is what it is — without being touched. The
/// consequence is intentional: this reports real numbers exactly when there is real work to see.
/// </remarks>
public sealed partial class LinuxAmdXdnaNpuUtilizationReader : IComputeUtilizationReader
{
    private const string AmdXdnaDriverName = "amdxdna";

    // DRM_IOCTL_AMDXDNA_GET_INFO = DRM_IOWR('d', DRM_COMMAND_BASE + 7, struct amdxdna_drm_get_info)
    // = (3 << 30) | (16 << 16) | (0x64 << 8) | 0x47.
    private const uint DrmIoctlAmdXdnaGetInfo = 0xC0106447;

    /// <summary>DRM_AMDXDNA_QUERY_SENSORS.</summary>
    private const uint QuerySensors = 4;

    /// <summary>AMDXDNA_SENSOR_TYPE_COLUMN_UTILIZATION; type 0 is total power, which is not wanted here.</summary>
    private const byte SensorTypeColumnUtilization = 1;

    /// <summary>sizeof(struct amdxdna_drm_query_sensor).</summary>
    private const int SensorRecordSize = 168;

    /// <summary>
    /// One power record plus up to eight columns.
    /// </summary>
    /// <remarks>
    /// Sized generously on purpose. The kernel copies the first record BEFORE it validates the buffer size,
    /// so a buffer smaller than one record is a genuine overrun of our own allocation rather than a graceful
    /// rejection — the declared size is not a bound the driver respects on that first write.
    /// </remarks>
    private const int SensorBufferSize = 9 * SensorRecordSize;

    private const int OpenReadWrite = 2;
    private const int OpenCloseOnExec = 0x80000;

    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<LinuxAmdXdnaNpuUtilizationReader> _logger;
    private readonly LinuxAccelSysfs _accel;
    private readonly string _deviceNodeRoot;
    private readonly Stopwatch _sinceDeviceRefresh = Stopwatch.StartNew();

    private List<XdnaDevice> _devices = [];
    private bool _enumerated;
    private bool _loggedSampleFailure;

    public LinuxAmdXdnaNpuUtilizationReader(
        ILogger<LinuxAmdXdnaNpuUtilizationReader> logger,
        string sysfsRoot = DrmSysfs.DefaultSysfsRoot,
        string deviceNodeRoot = "/dev/accel")
    {
        _logger = logger;
        _accel = new LinuxAccelSysfs(sysfsRoot);
        _deviceNodeRoot = deviceNodeRoot;
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
                if (device.SensorsUnsupported)
                {
                    continue;
                }

                // A suspended NPU is 0% busy, and asking would resume it — see the class remarks.
                var utilization = IsRuntimeSuspended(device) ? 0d : ReadUtilizationPercent(device);
                if (utilization is not null)
                {
                    samples.Add(new ComputeDeviceUtilization
                    {
                        DeviceKey = device.DeviceKey,
                        Kind = ComputeDeviceKind.Npu,
                        DisplayName = device.DisplayName,
                        UtilizationPercent = utilization.Value,
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
                _logger.LogWarning(exception, "AMD NPU utilization could not be sampled; the device will report no readings.");
            }

            return [];
        }
    }

    private static bool IsRuntimeSuspended(XdnaDevice device) =>
        string.Equals(DrmSysfs.ReadAttribute(device.RuntimeStatusPath), "suspended", StringComparison.OrdinalIgnoreCase);

    private unsafe double? ReadUtilizationPercent(XdnaDevice device)
    {
        var fd = -1;
        try
        {
            fd = Open(device.DeviceNodePath, OpenReadWrite | OpenCloseOnExec);
            if (fd < 0)
            {
                return null;
            }

            var buffer = stackalloc byte[SensorBufferSize];
            new Span<byte>(buffer, SensorBufferSize).Clear();

            var request = new AmdXdnaGetInfo
            {
                Param = QuerySensors,
                BufferSize = SensorBufferSize,
                Buffer = (ulong)buffer,
            };

            if (Ioctl(fd, DrmIoctlAmdXdnaGetInfo, &request) != 0)
            {
                // The overwhelmingly likely causes are permanent for this boot: a kernel whose driver has no
                // sensor support, or a part whose metrics table is not wired up. Latch off rather than
                // issuing a failing ioctl — which still resumes the NPU — once a second forever.
                device.SensorsUnsupported = true;
                _logger.LogInformation(
                    "The AMD NPU at {Device} did not answer the sensor query, so its utilization will not be reported. This needs a kernel new enough to expose column utilization, with AMD platform-management support present.",
                    device.DeviceKey);
                return null;
            }

            // On success the kernel overwrites BufferSize with the byte count it actually wrote.
            var recordCount = (int)Math.Min(request.BufferSize / SensorRecordSize, (uint)(SensorBufferSize / SensorRecordSize));
            return AverageColumnUtilization(new ReadOnlySpan<byte>(buffer, SensorBufferSize), recordCount);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            device.SensorsUnsupported = true;
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

    /// <summary>
    /// Averages the per-column busy percentages.
    /// </summary>
    /// <remarks>
    /// The MEAN, not the maximum. Each column is an independently schedulable slice of the array, so work
    /// occupying four of eight columns genuinely uses half the NPU; reporting the maximum would show 100% for
    /// a single-column inference and drive a fan curve on a number that is not the device's load. This is the
    /// opposite choice from the GPU engine readers, where concurrent engines make the maximum the honest one.
    /// </remarks>
    private static double? AverageColumnUtilization(ReadOnlySpan<byte> buffer, int recordCount)
    {
        var total = 0d;
        var columns = 0;

        for (var index = 0; index < recordCount; index++)
        {
            var record = buffer.Slice(index * SensorRecordSize, SensorRecordSize);
            var sensor = MemoryMarshal.Read<AmdXdnaQuerySensor>(record);

            if (sensor.Type != SensorTypeColumnUtilization)
            {
                continue;
            }

            // The platform-metrics stub fills its buffer with 0xFF when unsupported, so an out-of-range value
            // means the whole sample is untrustworthy — not that the NPU is busy beyond capacity.
            if (sensor.Input > 100)
            {
                return null;
            }

            total += sensor.Input;
            columns++;
        }

        return columns == 0 ? null : Math.Clamp(total / columns, 0d, 100d);
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
            List<XdnaDevice> devices = [];
            foreach (var device in _accel.EnumerateDevices())
            {
                if (!string.Equals(device.Driver, AmdXdnaDriverName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var nodePath = Path.Combine(_deviceNodeRoot, device.NodeName);
                if (!File.Exists(nodePath))
                {
                    continue;
                }

                devices.Add(new XdnaDevice
                {
                    DeviceKey = device.DeviceKey,
                    DisplayName = device.DisplayName,
                    DeviceNodePath = nodePath,
                    RuntimeStatusPath = Path.Combine(device.DevicePath, "power", "runtime_status"),
                });
            }

            _devices = devices;

            if (devices.Count > 0)
            {
                _logger.LogInformation("AMD NPU utilization: {Count} amdxdna device(s) found.", devices.Count);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Enumerating amdxdna devices failed; AMD NPU utilization will be unavailable.");
            _devices = [];
        }
    }

    public void Dispose()
    {
        // The device node is opened and closed per sample rather than held: an open handle participates in
        // the driver's context lifetime, and this reader must not influence the device it observes.
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int Ioctl(int fd, nuint request, void* argument);

    private static unsafe int Ioctl(int fd, uint request, AmdXdnaGetInfo* argument) => Ioctl(fd, request, (void*)argument);

    /// <summary>struct amdxdna_drm_get_info — 16 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AmdXdnaGetInfo
    {
        public uint Param;
        public uint BufferSize;
        public ulong Buffer;
    }

    /// <summary>
    /// struct amdxdna_drm_query_sensor — 168 bytes.
    /// </summary>
    /// <remarks>
    /// Only the leading label and the numeric fields matter here; the trailing status/units/pad are declared
    /// so the struct's size matches the kernel's exactly, because the records are read from a packed array.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = SensorRecordSize)]
    private unsafe struct AmdXdnaQuerySensor
    {
        public fixed byte Label[64];
        public uint Input;
        public uint Max;
        public uint Average;
        public uint Highest;
        public fixed byte Status[64];
        public fixed byte Units[16];
        public sbyte UnitModifier;
        public byte Type;
        public fixed byte Pad[6];
    }

    private sealed class XdnaDevice
    {
        public required string DeviceKey { get; init; }

        public required string DisplayName { get; init; }

        public required string DeviceNodePath { get; init; }

        public required string RuntimeStatusPath { get; init; }

        /// <summary>Set once the query fails, so a permanently unsupported kernel is asked only one time.</summary>
        public bool SensorsUnsupported { get; set; }
    }
}
