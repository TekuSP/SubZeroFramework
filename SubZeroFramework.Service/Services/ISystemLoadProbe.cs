using System.Diagnostics;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Reports how much of the machine is busy, and how much of that is this process.
/// </summary>
/// <remarks>
/// Exists so the load generator can aim at a share of the MACHINE rather than a share of itself. Those are
/// the same number only on an idle machine: a generator holding itself at 80% while the user's build takes
/// another 20% leaves the machine saturated, which is exactly the state the target was chosen to avoid.
/// </remarks>
public interface ISystemLoadProbe
{
    /// <summary>Everything the machine is doing, 0–1, or null where it cannot be read.</summary>
    double? TotalCpuUtilizationFraction { get; }

    /// <summary>This process's share of the whole machine, 0–1.</summary>
    double OwnCpuUtilizationFraction { get; }
}

/// <summary>
/// Measures system load through a control-telemetry reader, and this process through its own CPU time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owns a private reader instance</b>, rather than sharing the one the telemetry worker uses. Those
/// readers differentiate cumulative counters against their own previous call, so a second caller does not
/// merely observe — it consumes the interval, leaving the worker measuring utilisation over whatever
/// milliseconds happen to remain. The cost of a second instance is a few counter handles.
/// </para>
/// <para>
/// Sampling is rate-limited for the same reason in reverse: differencing over a very short window amplifies
/// noise into figures that swing wildly, and this one feeds a control loop.
/// </para>
/// </remarks>
public sealed class ControlTelemetrySystemLoadProbe : ISystemLoadProbe, IDisposable
{
    /// <summary>Shortest interval between reads, so each difference spans enough time to mean something.</summary>
    private static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromMilliseconds(400);

    private readonly IControlTelemetryReader _reader;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Lock _sampleLock = new();

    private readonly Stopwatch _sinceLastSample = Stopwatch.StartNew();
    private TimeSpan _lastProcessorTime;
    private double? _totalFraction;
    private double _ownFraction;
    private bool _disposed;

    public ControlTelemetrySystemLoadProbe(IControlTelemetryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
        _lastProcessorTime = _process.TotalProcessorTime;
    }

    public double? TotalCpuUtilizationFraction
    {
        get
        {
            Refresh();
            lock (_sampleLock)
            {
                return _totalFraction;
            }
        }
    }

    public double OwnCpuUtilizationFraction
    {
        get
        {
            Refresh();
            lock (_sampleLock)
            {
                return _ownFraction;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
        _process.Dispose();
    }

    private void Refresh()
    {
        lock (_sampleLock)
        {
            if (_disposed || _sinceLastSample.Elapsed < MinimumSampleInterval)
            {
                return;
            }

            var elapsed = _sinceLastSample.Elapsed;
            _sinceLastSample.Restart();

            try
            {
                _totalFraction = _reader.Sample().CpuUtilizationFraction;

                _process.Refresh();
                var processorTime = _process.TotalProcessorTime;
                var consumed = processorTime - _lastProcessorTime;
                _lastProcessorTime = processorTime;

                _ownFraction = Math.Clamp(
                    consumed.TotalSeconds / (elapsed.TotalSeconds * Environment.ProcessorCount),
                    0d,
                    1d);
            }
            catch (Exception)
            {
                // A probe that throws would take down the load it is supposed to be moderating. Unreadable
                // means "no information", which the caller degrades to its fixed target.
                _totalFraction = null;
            }
        }
    }
}
