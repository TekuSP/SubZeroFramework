// Compiled only into the windows TFM of Core. ADLX is a Windows-only AMD driver component.
#if WINDOWS10_0_26100_0_OR_GREATER
using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Supplies AMD GPU power, temperature and core clock on Windows through ADLX.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="WindowsNvmlGpuUtilizationReader"/>, closing the same gap for the other
/// vendor: the PDH <c>GPU Engine</c> counter set reports utilisation and memory for every adapter and has no
/// power, temperature or clock at all.
/// </para>
/// <para>
/// Unlike the NVML reader this samples INLINE rather than on a background thread, because the hazard that
/// forced that design does not exist here. A discrete NVIDIA GPU power-gates when idle and an NVML call wakes
/// it, costing ~500 ms; an integrated AMD GPU is always powered, and a measured sample costs ~0.14 ms. Adding
/// a background sampler would be machinery guarding against a problem this reader does not have.
/// </para>
/// <para>
/// The one expensive step is ADLX initialisation (~234 ms), which happens once on the first sample and is
/// never repeated — including after a failure, so a machine without an AMD driver pays it at most once.
/// </para>
/// </remarks>
public sealed class WindowsAdlxGpuUtilizationReader : IComputeUtilizationReader
{
    private readonly ILogger<WindowsAdlxGpuUtilizationReader> _logger;
    private readonly Lock _syncLock = new();

    private AdlxLibrary? _library;
    private bool _loadAttempted;
    private bool _loggedSampleFailure;

    /// <summary>The devices the last successful read reported, so the sleep gate has keys to ask about.</summary>
    private IReadOnlyList<ComputeDeviceIdentity> _lastSeenDevices = [];
    private bool _disposed;

    public WindowsAdlxGpuUtilizationReader(ILogger<WindowsAdlxGpuUtilizationReader> logger)
        => _logger = logger;

    public bool IsAvailable
    {
        get
        {
            lock (_syncLock)
            {
                return !_disposed && EnsureLibrary() is not null;
            }
        }
    }

    public IReadOnlyList<ComputeDeviceUtilization> Sample()
    {
        lock (_syncLock)
        {
            if (_disposed || EnsureLibrary() is not { } library)
            {
                return [];
            }

            // Skip a suspended GPU entirely: an ADLX call against one wakes it, the same hazard the NVIDIA
            // reader has. Gated on the keys the LAST read returned, because ADLX is what reports them — so
            // the first read always proceeds and every read after it can be suppressed.
            if (ComputeDeviceSleepGate.AreAllAsleep(_lastSeenDevices))
            {
                return [];
            }

            try
            {
                List<ComputeDeviceUtilization> devices = [];

                foreach (var reading in library.Read())
                {
                    devices.Add(new ComputeDeviceUtilization
                    {
                        // ADLX's PNPString is the Windows device instance path — the same key the PDH reader
                        // publishes under — so the composite merges the two into one entry for the GPU.
                        DeviceKey = reading.DeviceInstancePath,
                        Kind = ComputeDeviceKind.Gpu,
                        DisplayName = string.IsNullOrWhiteSpace(reading.Name) ? "AMD graphics" : reading.Name,

                        // PDH publishes first and enrichment never overwrites an answered field, so PDH's
                        // busy-time figure wins where both are present. This is the fallback for a GPU PDH
                        // could not see.
                        UtilizationPercent = reading.UtilizationPercent ?? 0d,
                        PowerWatts = reading.PowerWatts,
                        TemperatureCelsius = reading.TemperatureCelsius,
                        HotspotTemperatureCelsius = reading.HotspotTemperatureCelsius,
                        CoreClockMegahertz = reading.CoreClockMegahertz,

                        // From IADLXGPUMetricsSupport rather than the metrics interface: the former reports
                        // what the clock CAN be, the latter only what it is.
                        MaxCoreClockMegahertz = reading.MaxCoreClockMegahertz,
                        VramUsedBytes = reading.VramUsedBytes,
                        VramTotalBytes = reading.VramTotalBytes,

                        // ADLX reports no throttle-reason bitmask; null means "could not be asked", which is
                        // deliberately different from None.
                        ThrottleReasons = null,
                    });
                }

                // Remembered so the next call can ask Windows whether these are still powered up, without
                // going through ADLX to find out which devices exist.
                _lastSeenDevices = [.. devices.Select(static device => new ComputeDeviceIdentity
                {
                    DeviceKey = device.DeviceKey,
                    Kind = device.Kind,
                    DisplayName = device.DisplayName,
                })];

                return devices;
            }
            catch (Exception exception)
            {
                if (!_loggedSampleFailure)
                {
                    _loggedSampleFailure = true;
                    _logger.LogWarning(exception, "AMD GPU telemetry could not be sampled; those readings will be unavailable.");
                }

                return [];
            }
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _library?.Dispose();
            _library = null;
        }
    }

    private AdlxLibrary? EnsureLibrary()
    {
        if (_library is not null)
        {
            return _library;
        }

        // One attempt for the life of the object. A machine without the AMD driver will not grow one, and
        // retrying a ~234 ms initialisation on every sample would be a self-inflicted stall.
        if (_loadAttempted)
        {
            return null;
        }

        _loadAttempted = true;
        _library = AdlxLibrary.TryLoad(_logger);

        if (_library is null)
        {
            _logger.LogDebug("ADLX is unavailable; AMD GPU power and temperature will not be reported.");
        }

        return _library;
    }
}
#endif
