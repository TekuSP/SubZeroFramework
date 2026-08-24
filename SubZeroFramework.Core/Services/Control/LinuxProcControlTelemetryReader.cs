using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Control;

/// <summary>
/// Reads CPU utilisation, the clock-performance ratio and package power on Linux, straight from
/// <c>/proc</c> and <c>/sys</c>.
/// </summary>
/// <remarks>
/// <para>
/// All three sources are cumulative counters or instantaneous file reads — no subprocess, no sleep, no
/// interop. A tick is a handful of small reads from tmpfs-backed pseudo-files.
/// </para>
/// <para>
/// Utilisation and package power both need a previous sample to difference against, so the first tick after
/// construction reports neither. Package power additionally has to survive the RAPL counter wrapping at
/// <c>max_energy_range_uj</c>, which on a laptop happens every few minutes — treating a wrap as a negative
/// delta would report a large negative power exactly when the machine is busiest.
/// </para>
/// </remarks>
public sealed partial class LinuxProcControlTelemetryReader : IControlTelemetryReader
{
    private const string DefaultProcRoot = "/proc";
    private const string DefaultSysRoot = "/sys";

    private readonly ILogger<LinuxProcControlTelemetryReader> _logger;
    private readonly string _procRoot;
    private readonly string _sysRoot;
    private readonly Func<long> _timestampProvider;

    private CpuTimes? _previousAggregate;
    private CpuTimes[]? _previousPerCore;

    private double? _baseFrequencyKilohertz;
    private bool _resolvedBaseFrequency;

    private long? _previousEnergyMicrojoules;
    private long _previousEnergyTimestamp;
    private double? _energyRangeMicrojoules;
    private bool _resolvedEnergyRange;

    private bool _loggedUtilizationFailure;
    private bool _loggedFrequencyFailure;
    private bool _loggedPackagePowerFailure;

    public LinuxProcControlTelemetryReader(
        ILogger<LinuxProcControlTelemetryReader> logger,
        string procRoot = DefaultProcRoot,
        string sysRoot = DefaultSysRoot,
        Func<long>? timestampProvider = null)
    {
        _logger = logger;
        _procRoot = procRoot;
        _sysRoot = sysRoot;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    }

    private string ProcStatPath => Path.Combine(_procRoot, "stat");

    private string CpuDeviceRoot => Path.Combine(_sysRoot, "devices", "system", "cpu");

    private string PowercapRoot => Path.Combine(_sysRoot, "class", "powercap");

    /// <summary>
    /// True when <c>/proc/stat</c> is readable. Deliberately keyed on utilisation alone: the frequency and
    /// power sources are each optional refinements, and a machine with neither still gives the controller the
    /// signal it most needs.
    /// </summary>
    public bool IsAvailable => File.Exists(ProcStatPath);

    public ControlTelemetrySample Sample()
    {
        var (aggregate, perCore) = ReadUtilization();

        return new ControlTelemetrySample
        {
            CpuUtilizationFraction = aggregate,
            PerCoreUtilizationFraction = perCore,
            CpuPerformanceRatio = ReadPerformanceRatio(),
            CpuPackagePowerWatts = ReadPackagePowerWatts(),
        };
    }

    public void Dispose()
    {
    }

    private (double? Aggregate, ImmutableArray<double> PerCore) ReadUtilization()
    {
        try
        {
            var lines = File.ReadAllLines(ProcStatPath);

            CpuTimes? currentAggregate = null;
            var currentPerCore = new List<CpuTimes>();

            foreach (var line in lines)
            {
                if (!line.StartsWith("cpu", StringComparison.Ordinal))
                {
                    // The cpu lines come first in /proc/stat, so the first non-cpu line ends the section.
                    break;
                }

                if (!TryParseCpuTimes(line, out var times))
                {
                    continue;
                }

                // "cpu " (aggregate) versus "cpu0", "cpu1", … (per logical processor).
                if (line.StartsWith("cpu ", StringComparison.Ordinal))
                {
                    currentAggregate = times;
                }
                else
                {
                    currentPerCore.Add(times);
                }
            }

            var aggregate = ComputeBusyFraction(_previousAggregate, currentAggregate);
            var perCore = ComputePerCoreBusyFractions(currentPerCore);

            _previousAggregate = currentAggregate;
            _previousPerCore = [.. currentPerCore];

            return (aggregate, perCore);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            if (!_loggedUtilizationFailure)
            {
                _loggedUtilizationFailure = true;
                LogUtilizationFailure(exception, ProcStatPath);
            }

            return (null, []);
        }
    }

    private ImmutableArray<double> ComputePerCoreBusyFractions(List<CpuTimes> current)
    {
        // A core count that changes between ticks (CPU hotplug, or a container's cgroup being resized) makes
        // index-to-index differencing meaningless, so start over rather than report nonsense for every core.
        if (_previousPerCore is not { } previous || previous.Length != current.Count)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<double>(current.Count);
        for (var index = 0; index < current.Count; index++)
        {
            builder.Add(ComputeBusyFraction(previous[index], current[index]) ?? 0d);
        }

        return builder.MoveToImmutable();
    }

    private static double? ComputeBusyFraction(CpuTimes? previous, CpuTimes? current)
    {
        if (previous is not { } before || current is not { } after)
        {
            return null;
        }

        var totalDelta = after.Total - before.Total;
        var idleDelta = after.Idle - before.Idle;

        // A zero window means the counters did not advance — no information, rather than 0% busy.
        if (totalDelta <= 0)
        {
            return null;
        }

        return Math.Clamp(1d - ((double)idleDelta / totalDelta), 0d, 1d);
    }

    private static bool TryParseCpuTimes(string line, out CpuTimes times)
    {
        times = default;

        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // label + user nice system idle iowait irq softirq steal
        if (fields.Length < 9)
        {
            return false;
        }

        long total = 0;
        for (var index = 1; index <= 8; index++)
        {
            if (!long.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            total += value;
        }

        // guest and guest_nice are already counted inside user and nice, so summing only the first eight
        // fields avoids double counting them.
        if (!long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle)
            || !long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ioWait))
        {
            return false;
        }

        times = new CpuTimes(Total: total, Idle: idle + ioWait);
        return true;
    }

    private double? ReadPerformanceRatio()
    {
        try
        {
            if (!Directory.Exists(CpuDeviceRoot))
            {
                return null;
            }

            var baseKilohertz = ResolveBaseFrequencyKilohertz();
            if (baseKilohertz is not > 0d)
            {
                return null;
            }

            double sum = 0;
            var count = 0;

            foreach (var cpuDirectory in Directory.EnumerateDirectories(CpuDeviceRoot, "cpu*"))
            {
                var currentPath = Path.Combine(cpuDirectory, "cpufreq", "scaling_cur_freq");
                if (TryReadDouble(currentPath, out var currentKilohertz))
                {
                    sum += currentKilohertz;
                    count++;
                }
            }

            return count > 0 ? sum / count / baseKilohertz.Value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_loggedFrequencyFailure)
            {
                _loggedFrequencyFailure = true;
                LogFrequencyFailure(exception, CpuDeviceRoot);
            }

            return null;
        }
    }

    private double? ResolveBaseFrequencyKilohertz()
    {
        if (_resolvedBaseFrequency)
        {
            return _baseFrequencyKilohertz;
        }

        _resolvedBaseFrequency = true;

        foreach (var cpuDirectory in Directory.EnumerateDirectories(CpuDeviceRoot, "cpu*"))
        {
            // base_frequency is the rated (non-turbo) clock and is what makes the ratio a throttle signal.
            // cpuinfo_max_freq is the turbo ceiling, so a ratio against it reads below 1 even on a perfectly
            // healthy machine — usable as a fallback, but only as one.
            var basePath = Path.Combine(cpuDirectory, "cpufreq", "base_frequency");
            if (TryReadDouble(basePath, out var baseKilohertz) && baseKilohertz > 0d)
            {
                _baseFrequencyKilohertz = baseKilohertz;
                return _baseFrequencyKilohertz;
            }

            var maxPath = Path.Combine(cpuDirectory, "cpufreq", "cpuinfo_max_freq");
            if (TryReadDouble(maxPath, out var maxKilohertz) && maxKilohertz > 0d)
            {
                _baseFrequencyKilohertz = maxKilohertz;
                return _baseFrequencyKilohertz;
            }
        }

        return _baseFrequencyKilohertz;
    }

    private double? ReadPackagePowerWatts()
    {
        try
        {
            var zone = ResolvePackageZone();
            if (zone is null || !TryReadLong(Path.Combine(zone, "energy_uj"), out var energyMicrojoules))
            {
                return null;
            }

            var timestamp = _timestampProvider();
            var previousEnergy = _previousEnergyMicrojoules;
            var previousTimestamp = _previousEnergyTimestamp;

            _previousEnergyMicrojoules = energyMicrojoules;
            _previousEnergyTimestamp = timestamp;

            if (previousEnergy is not { } before)
            {
                return null;
            }

            var elapsedSeconds = (timestamp - previousTimestamp) / (double)Stopwatch.Frequency;

            return RaplEnergyMath.ComputeWatts(
                before,
                energyMicrojoules,
                elapsedSeconds,
                ResolveEnergyRangeMicrojoules(zone));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_loggedPackagePowerFailure)
            {
                _loggedPackagePowerFailure = true;
                LogPackagePowerFailure(exception, PowercapRoot);
            }

            return null;
        }
    }

    /// <summary>
    /// Finds the package-level RAPL zone. Only top-level <c>intel-rapl:N</c> zones are considered: the nested
    /// <c>intel-rapl:0:0</c> subzones are cores/uncore/dram slices of the same budget, and adding them to the
    /// package total would double count.
    /// </summary>
    private string? ResolvePackageZone()
    {
        if (!Directory.Exists(PowercapRoot))
        {
            return null;
        }

        foreach (var zone in Directory.EnumerateDirectories(PowercapRoot, "intel-rapl:*"))
        {
            if (!RaplEnergyMath.IsPackageZoneName(Path.GetFileName(zone)))
            {
                continue;
            }

            if (File.Exists(Path.Combine(zone, "energy_uj")))
            {
                return zone;
            }
        }

        return null;
    }

    private double? ResolveEnergyRangeMicrojoules(string zone)
    {
        if (_resolvedEnergyRange)
        {
            return _energyRangeMicrojoules;
        }

        _resolvedEnergyRange = true;

        if (TryReadDouble(Path.Combine(zone, "max_energy_range_uj"), out var range) && range > 0d)
        {
            _energyRangeMicrojoules = range;
        }

        return _energyRangeMicrojoules;
    }

    private static bool TryReadDouble(string path, out double value)
    {
        // Every sysfs attribute read here (kHz, microjoules) is an integer, so there is no fractional form to
        // parse and no locale to get wrong.
        var read = TryReadLong(path, out var parsed);
        value = read ? parsed : 0d;
        return read;
    }

    private static bool TryReadLong(string path, out long value)
    {
        value = 0;

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return long.TryParse(
                File.ReadAllText(path).AsSpan().Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Individual sysfs attributes disappear as devices come and go; that is not a reader failure.
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read CPU utilisation from {Path}; the adaptive controller will run without it.")]
    private partial void LogUtilizationFailure(Exception exception, string path);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read CPU frequency under {Path}; throttle detection falls back to other signals.")]
    private partial void LogFrequencyFailure(Exception exception, string path);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read CPU package power under {Path}; feed-forward falls back to adapter power.")]
    private partial void LogPackagePowerFailure(Exception exception, string path);

    private readonly record struct CpuTimes(long Total, long Idle);
}
