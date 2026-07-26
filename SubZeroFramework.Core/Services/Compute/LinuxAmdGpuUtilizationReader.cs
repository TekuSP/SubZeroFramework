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
public sealed class LinuxAmdGpuUtilizationReader : IComputeUtilizationReader
{
    private const string AmdGpuDriverName = "amdgpu";

    /// <summary>The device set is near-static; re-enumerating on a timer catches an eGPU being plugged in.</summary>
    private static readonly TimeSpan DeviceRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger<LinuxAmdGpuUtilizationReader> _logger;
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
                    continue;
                }

                samples.Add(new ComputeDeviceUtilization
                {
                    DeviceKey = device.DeviceKey,
                    Kind = ComputeDeviceKind.Gpu,
                    DisplayName = device.DisplayName,
                    UtilizationPercent = utilization.Value,
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
            });
        }

        _logger.LogInformation(
            "AMD GPU utilization: {Count} amdgpu device(s) reporting via gpu_busy_percent ({Devices}).",
            devices.Count,
            string.Join(", ", devices.Select(device => device.DisplayName)));

        return devices;
    }

    public void Dispose()
    {
        // Nothing held open: every sample is a fresh short read of a sysfs attribute.
    }

    private sealed record AmdGpuDevice
    {
        public required string DeviceKey { get; init; }

        public required string DisplayName { get; init; }

        public required string BusyPercentPath { get; init; }

        public required string RuntimeStatusPath { get; init; }
    }
}
