using System.Diagnostics;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Reads Intel NPU busy time from the ivpu driver's sysfs.
/// </summary>
/// <remarks>
/// <c>npu_busy_time_us</c> is a monotonic microsecond counter of how long the NPU had at least one job
/// outstanding, so busy share is the counter delta over the elapsed monotonic time. Cumulative since the
/// driver bound — it survives runtime PM cycles and system suspend, and resets only on module reload.
///
/// Two properties make this the well-behaved NPU source. Reading it touches no hardware: the driver returns
/// two in-memory timestamps under a mutex, with no runtime-PM reference and no firmware message, so polling a
/// suspended NPU is free and leaves it suspended. And the driver's own documentation asks for a sampling
/// period of about one second, because the lock it takes is also on the job-submit path — which is exactly
/// the cadence the telemetry tier uses. It must not be read faster.
///
/// SEMANTICS: this is a queue-non-empty duty cycle, not occupancy. One long serial job reads the same 100% as
/// a fully saturated array, which is why the UI calls it busy time rather than load.
///
/// The device node is deliberately never opened. Closing an ivpu accel handle forces a synchronous runtime
/// resume of the NPU, so a monitor that opened it would wake the very hardware it is only trying to observe.
/// </remarks>
public sealed partial class LinuxIntelNpuUtilizationReader : IComputeUtilizationReader
{
    private const string IvpuDriverName = "intel_vpu";
    private const string BusyTimeAttribute = "npu_busy_time_us";

    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<LinuxIntelNpuUtilizationReader> _logger;
    private readonly LinuxAccelSysfs _accel;
    private readonly Stopwatch _sinceDeviceRefresh = Stopwatch.StartNew();

    private List<IvpuDevice> _devices = [];
    private bool _enumerated;
    private bool _loggedSampleFailure;

    public LinuxIntelNpuUtilizationReader(
        ILogger<LinuxIntelNpuUtilizationReader> logger,
        string sysfsRoot = DrmSysfs.DefaultSysfsRoot)
    {
        _logger = logger;
        _accel = new LinuxAccelSysfs(sysfsRoot);
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

                // The first tick after startup has no previous counter to difference against, so a null
                // here is normal exactly once and a symptom every time after that.
                if (utilization is null)
                {
                    LogDeviceUnreadable(device.DisplayName, device.DeviceKey);
                }
                else
                {
                    LogDeviceSampled(device.DisplayName, utilization.Value);
                }

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
                _logger.LogWarning(exception, "Intel NPU utilization could not be sampled; the device will report no readings.");
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
            _devices = EnumerateDevices();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Enumerating Intel NPU devices failed; Intel NPU utilization will be unavailable.");
            _devices = [];
        }
    }

    private List<IvpuDevice> EnumerateDevices()
    {
        List<IvpuDevice> devices = [];

        foreach (var device in _accel.EnumerateDevices())
        {
            if (!string.Equals(device.Driver, IvpuDriverName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The attribute lives on the PCI device, not on the accel class node itself.
            var busyTimePath = Path.Combine(device.DevicePath, BusyTimeAttribute);
            if (!File.Exists(busyTimePath))
            {
                // Present before kernel 6.11; without it there is no honest utilization signal for this NPU.
                _logger.LogInformation(
                    "The Intel NPU at {Device} does not expose {Attribute} (kernel too old); its utilization cannot be reported, but the device is still listed.",
                    device.DeviceKey,
                    BusyTimeAttribute);
                continue;
            }

            devices.Add(new IvpuDevice(device.DeviceKey, device.DisplayName, busyTimePath));
        }

        if (devices.Count > 0)
        {
            _logger.LogInformation("Intel NPU utilization: {Count} device(s) reporting via {Attribute}.", devices.Count, BusyTimeAttribute);
        }

        return devices;
    }

    public void Dispose()
    {
        // Nothing held open: every sample is a short read of a sysfs attribute, by design.
    }

    private sealed class IvpuDevice(string deviceKey, string displayName, string busyTimePath)
    {
        private long _previousBusyMicroseconds;
        private long _previousTimestamp;
        private bool _primed;

        public string DeviceKey { get; } = deviceKey;

        public string DisplayName { get; } = displayName;

        public double? Sample()
        {
            var text = DrmSysfs.ReadAttribute(busyTimePath);
            if (text is null || !long.TryParse(text, out var busyMicroseconds))
            {
                return null;
            }

            // Monotonic, and the same clock base the kernel accumulated against: it excludes system-suspend
            // time exactly as the counter does, which the wall clock would not.
            var timestamp = Stopwatch.GetTimestamp();

            if (!_primed)
            {
                _primed = true;
                _previousBusyMicroseconds = busyMicroseconds;
                _previousTimestamp = timestamp;
                return null;
            }

            var busyDelta = busyMicroseconds - _previousBusyMicroseconds;
            var elapsedMicroseconds = (timestamp - _previousTimestamp) * 1_000_000d / Stopwatch.Frequency;

            _previousBusyMicroseconds = busyMicroseconds;
            _previousTimestamp = timestamp;

            if (busyDelta < 0)
            {
                // The counter restarted (module reload, or a rebind behind the same path). Reseeding and
                // reporting nothing is honest; treating the negative delta as 0% would claim an idle NPU.
                return null;
            }

            if (elapsedMicroseconds <= 0d)
            {
                return null;
            }

            // Clamp: the counter read and the clock read are not atomic, so a sub-millisecond overshoot on a
            // one-second window is expected.
            return Math.Clamp(busyDelta * 100d / elapsedMicroseconds, 0d, 100d);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "ivpu NPU {DisplayName} is {UtilizationPercent:F0}% busy.")]
    private partial void LogDeviceSampled(string displayName, double utilizationPercent);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "ivpu NPU {DisplayName} ({DeviceKey}) produced no ratio this tick; expected on the first sample, otherwise the busy-time counter did not advance usably.")]
    private partial void LogDeviceUnreadable(string displayName, string deviceKey);
}
