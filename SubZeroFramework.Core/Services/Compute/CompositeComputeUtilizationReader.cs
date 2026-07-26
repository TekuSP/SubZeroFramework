using Microsoft.Extensions.Logging;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Merges several per-vendor utilization readers into the single reader the provider consumes.
/// </summary>
/// <remarks>
/// Linux needs this where Windows does not: one PDH counter set covers every adapter on Windows, whereas each
/// Linux vendor has its own mechanism (amdgpu sysfs, NVML, the Intel PMU). A Framework 16 with the graphics
/// module fitted runs two of them at once.
///
/// Isolation is the point: a source that throws is dropped for that tick and the others still report. One
/// vendor's broken driver must not blank out the whole page. Devices are de-duplicated by
/// <see cref="ComputeDeviceUtilization.DeviceKey"/> — first source wins — so a GPU visible to two sources
/// (an AMD card readable through both sysfs and a future generic path) is published once.
/// </remarks>
public sealed partial class CompositeComputeUtilizationReader : IComputeUtilizationReader
{
    private readonly IReadOnlyList<IComputeUtilizationReader> _readers;
    private readonly ILogger<CompositeComputeUtilizationReader> _logger;
    private readonly HashSet<Type> _loggedFailures = [];

    public CompositeComputeUtilizationReader(
        IEnumerable<IComputeUtilizationReader> readers,
        ILogger<CompositeComputeUtilizationReader> logger)
    {
        _readers = [.. readers];
        _logger = logger;

        // "My GPU shows nothing" nearly always comes down to which readers were registered at all, so
        // record the composition once at startup rather than making it inferable from later silence.
        LogReadersRegistered(_readers.Count, string.Join(", ", _readers.Select(reader => reader.GetType().Name)));
    }

    public bool IsAvailable => _readers.Any(reader => SafeIsAvailable(reader));

    public IReadOnlyList<ComputeDeviceUtilization> Sample()
    {
        List<ComputeDeviceUtilization> merged = [];
        HashSet<string> seenDeviceKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (var reader in _readers)
        {
            IReadOnlyList<ComputeDeviceUtilization> samples;
            try
            {
                samples = reader.Sample();
            }
            catch (Exception exception)
            {
                LogOnce(reader, exception);
                continue;
            }

            var duplicates = 0;
            foreach (var sample in samples)
            {
                if (seenDeviceKeys.Add(sample.DeviceKey))
                {
                    merged.Add(sample);
                }
                else
                {
                    duplicates += 1;
                }
            }

            LogReaderSampled(reader.GetType().Name, samples.Count, duplicates);
        }

        return merged;
    }

    private bool SafeIsAvailable(IComputeUtilizationReader reader)
    {
        try
        {
            return reader.IsAvailable;
        }
        catch (Exception exception)
        {
            LogOnce(reader, exception);
            return false;
        }
    }

    private void LogOnce(IComputeUtilizationReader reader, Exception exception)
    {
        var readerType = reader.GetType();
        if (_loggedFailures.Add(readerType))
        {
            _logger.LogWarning(exception, "{Reader} failed and will be skipped; other GPU/NPU sources continue reporting.", readerType.Name);
        }
    }

    public void Dispose()
    {
        foreach (var reader in _readers)
        {
            try
            {
                reader.Dispose();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Disposing {Reader} failed.", reader.GetType().Name);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Composite GPU/NPU utilization is backed by {ReaderCount} reader(s): {Readers}.")]
    private partial void LogReadersRegistered(int readerCount, string readers);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "{Reader} reported {SampleCount} device(s), {DuplicateCount} of which another reader had already claimed.")]
    private partial void LogReaderSampled(string reader, int sampleCount, int duplicateCount);
}
