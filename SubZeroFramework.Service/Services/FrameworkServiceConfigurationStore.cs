using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using SubZeroFramework.Service.Models;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkServiceConfigurationStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonWriterOptions = new()
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ILogger<FrameworkServiceConfigurationStore> _logger;
    private bool _disposed;

    public FrameworkServiceConfigurationStore(ILogger<FrameworkServiceConfigurationStore> logger)
        : this(FrameworkServiceConfigurationPaths.GetPersistentConfigurationPath(), logger)
    {
    }

    public FrameworkServiceConfigurationStore(string persistentConfigurationPath, ILogger<FrameworkServiceConfigurationStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistentConfigurationPath);
        ArgumentNullException.ThrowIfNull(logger);

        PersistentConfigurationPath = Path.GetFullPath(persistentConfigurationPath);
        _logger = logger;
    }

    public string PersistentConfigurationPath { get; }

    public async Task WriteAsync(FrameworkServiceOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var root = await LoadRootObjectAsync(cancellationToken).ConfigureAwait(false);
            var frameworkServiceSection = root["FrameworkService"] as JsonObject ?? new JsonObject();

            frameworkServiceSection["PollingInterval"] = options.PollingInterval.ToString("c", CultureInfo.InvariantCulture);
            frameworkServiceSection["HardwareInfoPollingInterval"] = options.HardwareInfoPollingInterval.ToString("c", CultureInfo.InvariantCulture);
            frameworkServiceSection["AllowFanControlCommands"] = options.AllowFanControlCommands;
            root["FrameworkService"] = frameworkServiceSection;

            var directoryPath = Path.GetDirectoryName(PersistentConfigurationPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var temporaryPath = $"{PersistentConfigurationPath}.tmp";
            await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(JsonWriterOptions), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, PersistentConfigurationPath, overwrite: true);

            _logger.LogInformation(
                "Persisted service configuration overlay to {PersistentConfigurationPath}. PollingInterval={PollingInterval}, HardwareInfoPollingInterval={HardwareInfoPollingInterval}, AllowFanControlCommands={AllowFanControlCommands}.",
                PersistentConfigurationPath,
                options.PollingInterval,
                options.HardwareInfoPollingInterval,
                options.AllowFanControlCommands);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writeGate.Dispose();
        _disposed = true;
    }

    private async Task<JsonObject> LoadRootObjectAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(PersistentConfigurationPath))
        {
            return new JsonObject();
        }

        try
        {
            var json = await File.ReadAllTextAsync(PersistentConfigurationPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonObject();
            }

            var parsedRoot = JsonNode.Parse(json) as JsonObject;
            if (parsedRoot is not null)
            {
                return parsedRoot;
            }

            _logger.LogWarning("Persistent service configuration file {PersistentConfigurationPath} did not contain a JSON object root. Replacing it with a fresh configuration object.", PersistentConfigurationPath);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Persistent service configuration file {PersistentConfigurationPath} contained invalid JSON. Replacing it with a fresh configuration object.", PersistentConfigurationPath);
        }

        return new JsonObject();
    }
}
