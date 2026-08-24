using System.Diagnostics;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Reads AMD GPU busyness from the amdgpu driver's sysfs.
/// </summary>
/// <remarks>
/// <c>gpu_busy_percent</c> is the driver's own busy figure, already a 0–100 percentage over a short internal
/// window, so unlike the Windows counter path there is no delta arithmetic to do — read it and publish it.
/// It is present for both integrated (Radeon 890M) and discrete Radeon parts.
///
/// POWER: a discrete GPU in runtime suspend must not be woken just to be measured. Reading
/// <c>gpu_busy_percent</c> goes to the SMU and can resume the device, so a card whose runtime PM state reads
/// "suspended" is reported as 0% from the sysfs power state alone, which is what a sleeping GPU's busy share
/// actually is. This is the difference between a monitoring feature and a battery-life regression.
/// </remarks>
public sealed partial class LinuxAmdGpuUtilizationReader : IComputeUtilizationReader
{
    private const string AmdGpuDriverName = "amdgpu";

    /// <summary>The device set is near-static; re-enumerating on a timer catches an eGPU being plugged in.</summary>
    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<LinuxAmdGpuUtilizationReader> _logger;
    /// <summary>How many tempN_label slots to scan. amdgpu currently exposes edge, junction and mem.</summary>
    private const int MaximumHwmonSensorIndex = 8;

    private readonly DrmSysfs _sysfs;
    private readonly Stopwatch _sinceDeviceRefresh = Stopwatch.StartNew();
    private IReadOnlyList<AmdGpuDevice> _devices = [];
    private bool _enumerated;
    private bool _loggedSampleFailure;

    public LinuxAmdGpuUtilizationReader(ILogger<LinuxAmdGpuUtilizationReader> logger, string sysfsRoot = DrmSysfs.DefaultSysfsRoot)
    {
        _logger = logger;
        _sysfs = new DrmSysfs(sysfsRoot);
    }

    /// <summary>
    /// True once at least one amdgpu card exposing <c>gpu_busy_percent</c> has been found.
    /// </summary>
    /// <remarks>
    /// Not an <c>OperatingSystem.IsLinux()</c> check — the reader is plain file I/O over an injectable sysfs
    /// root, so gating on the OS would make it untestable off Linux. DI decides where it is constructed.
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
                var utilization = ReadUtilizationPercent(device);
                if (utilization is null)
                {
                    // Dropping the device here is what "the GPU card shows no reading" looks like from the
                    // UI, so name it rather than leaving an unexplained gap in the sample set.
                    LogDeviceUnreadable(device.DisplayName, device.DeviceKey);
                    continue;
                }

                LogDeviceSampled(device.DisplayName, utilization.Value);

                // The extended readings all go through the SMU, exactly as gpu_busy_percent does, so they are
                // gated behind the SAME runtime-PM check. Reading power off a suspended discrete GPU would
                // resume it — the precise battery-drain behaviour this reader was written to avoid — and the
                // answer would be meaningless anyway.
                ExtendedTelemetry? extended = IsRuntimeSuspended(device) ? null : ReadExtendedTelemetry(device);

                samples.Add(new ComputeDeviceUtilization
                {
                    DeviceKey = device.DeviceKey,
                    Kind = ComputeDeviceKind.Gpu,
                    DisplayName = device.DisplayName,
                    UtilizationPercent = utilization.Value,
                    PowerWatts = extended?.PowerWatts,
                    TemperatureCelsius = extended?.TemperatureCelsius,
                    HotspotTemperatureCelsius = extended?.HotspotTemperatureCelsius,
                    CoreClockMegahertz = extended?.CoreClockMegahertz,
                    MaxCoreClockMegahertz = device.MaximumCoreClockMegahertz,
                    VramUsedBytes = extended?.VramUsedBytes,
                    VramTotalBytes = device.VramTotalBytes,

                    // amdgpu exposes no throttle-reason attribute, so this stays null — "not reported" rather
                    // than None, which would claim the GPU is definitely not throttling.
                    ThrottleReasons = null,
                });
            }

            return samples;
        }
        catch (Exception exception)
        {
            if (!_loggedSampleFailure)
            {
                _loggedSampleFailure = true;
                _logger.LogWarning(exception, "AMD GPU utilization could not be sampled; the affected devices will report no readings.");
            }

            return [];
        }
    }

    private double? ReadUtilizationPercent(AmdGpuDevice device)
    {
        // A runtime-suspended GPU is definitionally 0% busy, and asking the SMU would resume it.
        if (IsRuntimeSuspended(device))
        {
            return 0d;
        }

        var raw = DrmSysfs.ReadInt64Attribute(device.BusyPercentPath);
        return raw is null ? null : Math.Clamp(raw.Value, 0L, 100L);
    }

    private static bool IsRuntimeSuspended(AmdGpuDevice device) =>
        string.Equals(DrmSysfs.ReadAttribute(device.RuntimeStatusPath), "suspended", StringComparison.OrdinalIgnoreCase);

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
            _logger.LogDebug(exception, "Enumerating amdgpu devices failed; AMD GPU utilization will be unavailable.");
            _devices = [];
        }
    }

    private IReadOnlyList<AmdGpuDevice> EnumerateDevices()
    {
        List<(string CardName, string DevicePath, DrmUevent Uevent)> candidates = [];

        foreach (var cardName in _sysfs.EnumerateCardNames())
        {
            var devicePath = _sysfs.GetCardDevicePath(cardName);
            var uevent = DrmUevent.Parse(DrmSysfs.ReadAttribute(Path.Combine(devicePath, "uevent")));

            if (!string.Equals(uevent.Driver, AmdGpuDriverName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A card without the attribute cannot be measured at all — some older APUs never expose it.
            if (!File.Exists(Path.Combine(devicePath, "gpu_busy_percent")))
            {
                _logger.LogDebug("amdgpu card {Card} has no gpu_busy_percent attribute; it will not report utilization.", cardName);
                continue;
            }

            candidates.Add((cardName, devicePath, uevent));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var pciNames = PciIdDatabase.Lookup(
            [.. candidates
                .Where(candidate => candidate.Uevent.VendorId is not null && candidate.Uevent.DeviceId is not null)
                .Select(candidate => new PciDeviceId(candidate.Uevent.VendorId!.Value, candidate.Uevent.DeviceId!.Value))
                .Distinct()]);

        List<AmdGpuDevice> devices = [];
        foreach (var (cardName, devicePath, uevent) in candidates)
        {
            var hwmonPath = ResolveHwmonPath(devicePath);
            var vendorId = uevent.VendorId ?? DrmSysfs.ReadHexIdAttribute(Path.Combine(devicePath, "vendor"));
            var deviceId = uevent.DeviceId ?? DrmSysfs.ReadHexIdAttribute(Path.Combine(devicePath, "device"));

            string? name = null;
            if (vendorId is not null && deviceId is not null
                && pciNames.TryGetValue(new PciDeviceId(vendorId.Value, deviceId.Value), out var names))
            {
                name = names.DeviceName ?? names.VendorName;
            }

            devices.Add(new AmdGpuDevice
            {
                // The PCI address is stable across reboots, which is what telemetry channels key on.
                DeviceKey = uevent.PciSlotName ?? cardName,
                DisplayName = name ?? $"AMD GPU ({cardName})",
                BusyPercentPath = Path.Combine(devicePath, "gpu_busy_percent"),
                RuntimeStatusPath = Path.Combine(devicePath, "power", "runtime_status"),
                HwmonPath = hwmonPath,
                EdgeTemperaturePath = ResolveTemperaturePath(hwmonPath, "edge"),
                HotspotTemperaturePath = ResolveTemperaturePath(hwmonPath, "junction"),
                VramUsedPath = Path.Combine(devicePath, "mem_info_vram_used"),

                // Both of these are FIXED for the life of the device, so they are read once here rather than
                // every tick — and reading them during enumeration is safe for the same reason the busy
                // attribute is not: neither goes to the SMU. mem_info_vram_total is a BAR-size property and
                // freq1_label/max is a driver-side table entry.
                VramTotalBytes = ReadPositiveBytes(Path.Combine(devicePath, "mem_info_vram_total")),
                MaximumCoreClockMegahertz = ReadMaximumCoreClockMegahertz(devicePath),
            });
        }

        _logger.LogInformation(
            "AMD GPU utilization: {Count} amdgpu device(s) reporting via gpu_busy_percent ({Devices}).",
            devices.Count,
            string.Join(", ", devices.Select(device => device.DisplayName)));

        return devices;
    }



    /// <summary>Reads power, temperatures, clock and video-memory use for a device that is known to be awake.</summary>
    private static ExtendedTelemetry ReadExtendedTelemetry(AmdGpuDevice device) => new(
        PowerWatts: ReadPowerWatts(device),
        TemperatureCelsius: ReadTemperatureCelsius(device.EdgeTemperaturePath),
        HotspotTemperatureCelsius: ReadTemperatureCelsius(device.HotspotTemperaturePath),
        CoreClockMegahertz: ReadCoreClockMegahertz(device),
        VramUsedBytes: ReadPositiveBytes(device.VramUsedPath));

    private readonly record struct ExtendedTelemetry(
        double? PowerWatts,
        double? TemperatureCelsius,
        double? HotspotTemperatureCelsius,
        double? CoreClockMegahertz,
        double? VramUsedBytes);

    /// <summary>
    /// Reads a sysfs byte count, rejecting a negative value.
    /// </summary>
    /// <remarks>
    /// Zero is KEPT: an idle GPU really can have no VRAM allocated, and mapping that to "unknown" would make
    /// the memory readout blink out whenever the card went quiet.
    /// </remarks>
    private static double? ReadPositiveBytes(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var bytes = DrmSysfs.ReadInt64Attribute(path);
        return bytes is null or < 0 ? null : bytes.Value;
    }

    /// <summary>
    /// Finds the card's hwmon directory. The <c>hwmonN</c> suffix is assigned by the kernel at probe time and
    /// varies with which drivers loaded first, so it is discovered rather than assumed.
    /// </summary>
    private static string? ResolveHwmonPath(string devicePath)
    {
        var hwmonRoot = Path.Combine(devicePath, "hwmon");

        try
        {
            return Directory.Exists(hwmonRoot)
                ? Directory.EnumerateDirectories(hwmonRoot, "hwmon*").OrderBy(static path => path, StringComparer.Ordinal).FirstOrDefault()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the <c>tempN_input</c> whose <c>tempN_label</c> matches, so "edge" and "junction" are identified
    /// by what the kernel calls them.
    /// </summary>
    /// <remarks>
    /// Resolving by label rather than by index is the whole point. The mapping is NOT fixed: amdgpu exposes
    /// edge/junction/mem on discrete cards but only a subset on APUs, so a card with no junction sensor would
    /// have its memory temperature silently read as a hotspot if index order were assumed — and memory runs
    /// far cooler, so the controller would conclude everything was fine.
    /// </remarks>
    private static string? ResolveTemperaturePath(string? hwmonPath, string label)
    {
        if (hwmonPath is null)
        {
            return null;
        }

        for (var index = 1; index <= MaximumHwmonSensorIndex; index++)
        {
            var labelPath = Path.Combine(hwmonPath, $"temp{index}_label");
            if (!string.Equals(DrmSysfs.ReadAttribute(labelPath), label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var inputPath = Path.Combine(hwmonPath, $"temp{index}_input");
            return File.Exists(inputPath) ? inputPath : null;
        }

        return null;
    }

    /// <summary>
    /// Board power draw in watts.
    /// </summary>
    /// <remarks>
    /// <c>power1_average</c> is preferred over <c>power1_input</c>: the instantaneous figure swings hard
    /// between samples, and a feed-forward term fed from it would chase noise. Not every ASIC exposes both,
    /// hence the fallback.
    /// </remarks>
    private static double? ReadPowerWatts(AmdGpuDevice device)
    {
        if (device.HwmonPath is null)
        {
            return null;
        }

        var microwatts = DrmSysfs.ReadInt64Attribute(Path.Combine(device.HwmonPath, "power1_average"))
            ?? DrmSysfs.ReadInt64Attribute(Path.Combine(device.HwmonPath, "power1_input"));

        // Zero is a real reading for an idle GPU, but a negative one is not a power figure.
        return microwatts is null or < 0 ? null : microwatts.Value / 1_000_000d;
    }

    /// <summary>Reads a hwmon temperature, which the ABI reports in millidegrees Celsius.</summary>
    private static double? ReadTemperatureCelsius(string? inputPath)
    {
        if (inputPath is null)
        {
            return null;
        }

        var millidegrees = DrmSysfs.ReadInt64Attribute(inputPath);
        return millidegrees is null ? null : millidegrees.Value / 1000d;
    }

    /// <summary>
    /// The card's highest schedulable core clock, from the DPM state table.
    /// </summary>
    /// <remarks>
    /// Read during enumeration, not per tick: the table is a fixed driver-side property. Absent on some APUs,
    /// which leaves the maximum null and the clock-versus-maximum bar hidden rather than showing a ratio
    /// against a guess.
    /// </remarks>
    private static double? ReadMaximumCoreClockMegahertz(string devicePath)
        => AmdGpuClockTable.ParseMaximumMegahertz(DrmSysfs.ReadAttribute(Path.Combine(devicePath, "pp_dpm_sclk")));

    /// <summary>Reads the core clock, which hwmon reports in hertz.</summary>
    private static double? ReadCoreClockMegahertz(AmdGpuDevice device)
    {
        if (device.HwmonPath is null)
        {
            return null;
        }

        var hertz = DrmSysfs.ReadInt64Attribute(Path.Combine(device.HwmonPath, "freq1_input"));
        return hertz is null or <= 0 ? null : hertz.Value / 1_000_000d;
    }

    public void Dispose()
    {
        // Nothing held open: every sample is a fresh short read of a sysfs attribute.
    }

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "amdgpu {DisplayName} is {UtilizationPercent:F0}% busy.")]
    private partial void LogDeviceSampled(string displayName, double utilizationPercent);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "amdgpu {DisplayName} ({DeviceKey}) returned no reading this tick and is omitted from the sample.")]
    private partial void LogDeviceUnreadable(string displayName, string deviceKey);

    private sealed record AmdGpuDevice
    {
        public required string DeviceKey { get; init; }

        public required string DisplayName { get; init; }

        public required string BusyPercentPath { get; init; }

        public required string RuntimeStatusPath { get; init; }

        /// <summary>
        /// The card's hwmon directory, or null when it has none. Resolved once during enumeration because the
        /// hwmonN suffix is assigned by the kernel at probe time and is not predictable.
        /// </summary>
        public string? HwmonPath { get; init; }

        /// <summary>Path to the edge (die) temperature, resolved by LABEL rather than by index.</summary>
        public string? EdgeTemperaturePath { get; init; }

        /// <summary>Path to the junction (hotspot) temperature, resolved by label.</summary>
        public string? HotspotTemperaturePath { get; init; }

        /// <summary>Path to <c>mem_info_vram_used</c>, read every tick — allocation moves with the workload.</summary>
        public string? VramUsedPath { get; init; }

        /// <summary>Total video memory, resolved ONCE: the BAR size does not change while the machine runs.</summary>
        public double? VramTotalBytes { get; init; }

        /// <summary>Highest schedulable core clock, resolved once from the DPM table.</summary>
        public double? MaximumCoreClockMegahertz { get; init; }
    }
}
