using System.Diagnostics;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Enriches Intel GPUs with power, temperature, clock, throttle state and video memory through IGCL —
/// closing the same gap NVML closes for NVIDIA and ADLX for AMD.
/// </summary>
/// <remarks>
/// <para>
/// Registered AFTER the PDH reader in the Windows composite and keyed on the same device instance path (the
/// PCI BDF from IGCL's adapter properties joins to the PnP identity), so an Intel adapter is enriched in
/// place rather than published twice. PDH keeps owning utilisation; this reader's own figure — the delta of
/// IGCL's busy-time counter — only surfaces if PDH somehow missed the adapter.
/// </para>
/// <para>
/// Samples INLINE on the calling tier, unlike the NVML reader's background thread: IGCL answers a device in
/// D3 with <c>CTL_RESULT_ERROR_DEVICE_UNAVAILABLE</c> instead of waking it, so there is no wake hazard and no
/// documented half-second stall to keep off the polling thread. That belief is from the header, not from
/// hardware — the call is timed and a slow one is logged, so if an Arc machine proves otherwise the log says
/// so on the first tick.
/// </para>
/// <para>
/// This matters for INTEGRATED Intel graphics above all: every Intel Framework (13, 12) ships a Core /
/// Core Ultra part with an Iris Xe or Arc iGPU, and on Linux those parts expose no power, energy or
/// temperature at all — both i915 and xe gate their whole hwmon registration on IS_DGFX. IGCL is therefore
/// the ONLY route to Intel GPU power and temperature on any Framework, and it exists only on Windows.
/// </para>
/// <para>
/// UNVERIFIED ON HARDWARE: the reference machine is a Framework 16 (AMD + NVIDIA) with no Intel GPU, so
/// this path has never executed against a real ControlLib. Every failure degrades to "no Intel telemetry"
/// while PDH continues to report utilisation, and each telemetry field is independently gated by IGCL's own
/// <c>bSupported</c> flag — so a part that reports only some of them still publishes those.
/// </para>
/// </remarks>
public sealed partial class WindowsIgclGpuUtilizationReader : IComputeUtilizationReader
{
    private const ushort IntelVendorId = 0x8086;

    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdentityRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>An inline call slower than this is worth a log line — it would be NVML-style stalling.</summary>
    private static readonly TimeSpan SlowCallThreshold = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<WindowsIgclGpuUtilizationReader> _logger;
    private readonly IComputeDeviceIdentityResolver _identityResolver;

    private IgclLibrary? _library;
    private bool _libraryProbed;
    private bool _loggedSampleFailure;
    private bool _loggedSlowCall;

    private IReadOnlyList<IgclDevice> _devices = [];
    private long _devicesResolvedAt;
    private bool _devicesResolved;

    private IReadOnlyList<ComputeDeviceIdentity> _identities = [];
    private long _identitiesResolvedAt;

    // Previous counter snapshots per device key, for the energy → power and busy-time → utilisation deltas.
    private readonly Dictionary<string, IgclTelemetrySnapshot> _previousByKey = new(StringComparer.OrdinalIgnoreCase);

    // The rated maximum clock is a fixed property of the part, but reading it costs a domain enumeration
    // plus a property call — so it is resolved once per device rather than on every tick. Cleared whenever
    // the device list is re-enumerated, since the handles it is keyed on do not survive that.
    private readonly Dictionary<IntPtr, double?> _maxClockByHandle = [];

    public WindowsIgclGpuUtilizationReader(
        ILogger<WindowsIgclGpuUtilizationReader> logger,
        IComputeDeviceIdentityResolver identityResolver)
    {
        _logger = logger;
        _identityResolver = identityResolver;
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
            if (_devices.Count == 0 || _library is null)
            {
                return [];
            }

            var identities = ResolveIdentities();

            // Skip a suspended GPU entirely. An IGCL call against one wakes it, exactly as an NVML call does
            // against a sleeping NVIDIA part — this reader simply never had the guard.
            if (ComputeDeviceSleepGate.AreAllAsleep(identities))
            {
                return [];
            }

            List<ComputeDeviceUtilization> samples = new(_devices.Count);

            var started = Stopwatch.GetTimestamp();
            foreach (var device in _devices)
            {
                var identity = identities.FirstOrDefault(candidate => WindowsPciAddress.Matches(candidate.PciAddress, device.PciAddress));

                // Without an OS identity there is no key that lines up with the PDH reader's. Publishing
                // under the PCI address instead would show the GPU twice — once per reader.
                if (identity is null)
                {
                    LogUnmatchedDevice(device.PciAddress ?? "<unknown>", device.Name ?? "<unnamed>");
                    continue;
                }

                var telemetry = _library.TryGetTelemetry(device.Handle);
                if (telemetry is null)
                {
                    // Includes a discrete Arc card in D3, which IGCL reports as an error rather than waking.
                    // Nothing is published: PDH still covers utilisation, and inventing zeros for power or
                    // temperature would read as measurements.
                    _previousByKey.Remove(identity.DeviceKey);
                    continue;
                }

                var previous = _previousByKey.GetValueOrDefault(identity.DeviceKey);
                _previousByKey[identity.DeviceKey] = telemetry;

                // Board energy where the card reports it (discrete), chip energy otherwise (integrated) —
                // consistently one or the other per device, because mixing them across ticks would make the
                // power figure jump by the board overhead.
                var (previousEnergy, currentEnergy) = telemetry.TotalCardEnergyJoules is not null
                    ? (previous?.TotalCardEnergyJoules, telemetry.TotalCardEnergyJoules)
                    : (previous?.GpuEnergyJoules, telemetry.GpuEnergyJoules);

                var (vramUsed, vramTotal) = _library.TryGetMemory(device.Handle);

                // The telemetry snapshot's temperature field and the dedicated sensor API are separate
                // driver paths, so a part that marks one unsupported may still answer on the other.
                var (sensorGpuCelsius, sensorHottestCelsius) = _library.TryGetTemperatures(device.Handle);

                samples.Add(new ComputeDeviceUtilization
                {
                    DeviceKey = identity.DeviceKey,
                    Kind = identity.Kind,
                    DisplayName = device.Name ?? identity.DisplayName,

                    // The composite keeps PDH's utilisation anyway (that reader publishes first and
                    // enrichment never overwrites); this figure stands only when PDH missed the adapter.
                    // 0 on the first tick, when no delta window exists yet.
                    UtilizationPercent = IgclCounterMath.AverageActivityPercent(
                        previous?.GlobalActivitySeconds, previous?.TimestampSeconds,
                        telemetry.GlobalActivitySeconds, telemetry.TimestampSeconds) ?? 0d,
                    PowerWatts = IgclCounterMath.AveragePowerWatts(
                        previousEnergy, previous?.TimestampSeconds,
                        currentEnergy, telemetry.TimestampSeconds),
                    TemperatureCelsius = telemetry.GpuTemperatureCelsius ?? sensorGpuCelsius,

                    // CTL_TEMP_SENSORS_GLOBAL is "the maximum across all device sensors" — the same notion
                    // the neutral model carries for amdgpu's junction sensor. Only published when it is
                    // actually hotter than the GPU reading, since an identical value is the same sensor
                    // reported twice rather than a second measurement.
                    HotspotTemperatureCelsius = sensorHottestCelsius > (telemetry.GpuTemperatureCelsius ?? sensorGpuCelsius)
                        ? sensorHottestCelsius
                        : null,
                    CoreClockMegahertz = telemetry.CoreClockMegahertz,
                    MaxCoreClockMegahertz = ResolveMaxClockMegahertz(device.Handle),
                    VramUsedBytes = vramUsed,
                    VramTotalBytes = vramTotal,
                    ThrottleReasons = MapThrottleReasons(telemetry),
                });
            }

            var duration = Stopwatch.GetElapsedTime(started);
            if (duration >= SlowCallThreshold && !_loggedSlowCall)
            {
                _loggedSlowCall = true;
                _logger.LogWarning(
                    "IGCL sampling took {Milliseconds:F0} ms inline; if this recurs the reader should move to a background sampler like NVML's.",
                    duration.TotalMilliseconds);
            }

            return samples;
        }
        catch (Exception exception)
        {
            if (!_loggedSampleFailure)
            {
                _loggedSampleFailure = true;
                _logger.LogWarning(exception, "Intel GPU telemetry could not be sampled; those readings will be unavailable.");
            }

            return [];
        }
    }

    /// <summary>The device's rated maximum core clock, resolved once and cached.</summary>
    private double? ResolveMaxClockMegahertz(IntPtr handle)
    {
        if (_maxClockByHandle.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        var megahertz = _library?.TryGetMaxClockMegahertz(handle);
        _maxClockByHandle[handle] = megahertz;
        return megahertz;
    }

    /// <summary>
    /// Maps IGCL's instantaneous limit indicators onto the vendor-neutral flags.
    /// </summary>
    /// <remarks>
    /// The indicators are plain fields with no per-field "supported" marker, so a successful telemetry call
    /// is taken as "the device reports throttling" — all-false becomes <see cref="ComputeThrottleReasons.None"/>,
    /// never null. Current and voltage limits have no dedicated flag in the neutral model and map to
    /// <see cref="ComputeThrottleReasons.Other"/>; low-utilisation downclocking is
    /// <see cref="ComputeThrottleReasons.Idle"/>, which the fan controller must not escalate on.
    /// </remarks>
    private static ComputeThrottleReasons MapThrottleReasons(IgclTelemetrySnapshot telemetry)
    {
        var reasons = ComputeThrottleReasons.None;

        if (telemetry.PowerLimited)
        {
            reasons |= ComputeThrottleReasons.PowerLimit;
        }

        if (telemetry.TemperatureLimited)
        {
            reasons |= ComputeThrottleReasons.ThermalLimit;
        }

        if (telemetry.CurrentLimited || telemetry.VoltageLimited)
        {
            reasons |= ComputeThrottleReasons.Other;
        }

        if (telemetry.UtilizationLimited)
        {
            reasons |= ComputeThrottleReasons.Idle;
        }

        return reasons;
    }

    private void EnsureDevices()
    {
        if (_devicesResolved && Stopwatch.GetElapsedTime(_devicesResolvedAt) < DeviceRefreshInterval)
        {
            return;
        }

        _devicesResolved = true;
        _devicesResolvedAt = Stopwatch.GetTimestamp();

        if (!_libraryProbed)
        {
            _libraryProbed = true;
            _library = IgclLibrary.TryLoad(_logger);
        }

        if (_library is null)
        {
            _devices = [];
            return;
        }

        try
        {
            // Both caches are keyed on things the old device list owned, so they go with it.
            _maxClockByHandle.Clear();
            _previousByKey.Clear();
            _devices = [.. _library.EnumerateDevices().Where(device => device.PciVendorId == IntelVendorId)];

            if (_devices.Count > 0)
            {
                LogDevicesEnumerated(_devices.Count, string.Join(", ", _devices.Select(device => device.Name ?? device.PciAddress ?? "<unnamed>")));
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Enumerating IGCL devices failed; Intel GPU telemetry will be unavailable.");
            _devices = [];
        }
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
            _logger.LogDebug(exception, "Resolving compute device identities failed; IGCL readings cannot be joined this tick.");
        }

        _identitiesResolvedAt = Stopwatch.GetTimestamp();
        return _identities;
    }

    public void Dispose()
    {
        _library?.Dispose();
        _library = null;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Intel GPU telemetry: IGCL reporting {Count} adapter(s) ({Devices}).")]
    private partial void LogDevicesEnumerated(int count, string devices);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "IGCL reported an adapter at {PciAddress} ({Name}) with no matching PnP device; its telemetry is not published.")]
    private partial void LogUnmatchedDevice(string pciAddress, string name);
}
