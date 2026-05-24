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

    private readonly ReactiveRequestQueue _writeQueue = new();
    private readonly ILogger<FrameworkServiceConfigurationStore> _logger;
    private readonly string _defaultPersistentConfigurationPath;
    private string _persistentConfigurationPath;
    private bool _disposed;

    public FrameworkServiceConfigurationStore(ILogger<FrameworkServiceConfigurationStore> logger)
        : this(FrameworkServiceConfigurationPaths.GetPersistentConfigurationPath(), logger)
    {
    }

    public FrameworkServiceConfigurationStore(string defaultPersistentConfigurationPath, ILogger<FrameworkServiceConfigurationStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPersistentConfigurationPath);
        ArgumentNullException.ThrowIfNull(logger);

        _defaultPersistentConfigurationPath = Path.GetFullPath(defaultPersistentConfigurationPath);
        _persistentConfigurationPath = StorePathBootstrap.ResolveActivePath(_defaultPersistentConfigurationPath);
        _logger = logger;
    }

    public string PersistentConfigurationPath => Volatile.Read(ref _persistentConfigurationPath);

    public string DefaultPersistentConfigurationPath => _defaultPersistentConfigurationPath;

    public Task<StoreRelocationResult> RelocateAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var current = Volatile.Read(ref _persistentConfigurationPath);
            var result = await StorePathRelocator.RelocateAsync(current, _defaultPersistentConfigurationPath, targetDirectory, ct).ConfigureAwait(false);
            if (result.Succeeded && !string.Equals(result.ActivePath, current, StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _persistentConfigurationPath, result.ActivePath);
                _logger.LogInformation("Relocated persistent service configuration store from {OldPath} to {NewPath}.", current, result.ActivePath);
            }
            else if (!result.Succeeded)
            {
                _logger.LogWarning("Persistent service configuration store relocation to '{TargetDirectory}' failed: {Message}", targetDirectory, result.Message);
            }

            return result;
        }, cancellationToken);
    }

    public Task<FrameworkServiceOptions?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var root = await LoadRootObjectAsync(ct).ConfigureAwait(false);
            if (root["FrameworkService"] is not JsonObject section)
            {
                return (FrameworkServiceOptions?)null;
            }

            var defaults = new FrameworkServiceOptions();

            return (FrameworkServiceOptions?)new FrameworkServiceOptions
            {
                PollingInterval = ReadTimeSpan(section, "PollingInterval", defaults.PollingInterval),
                HardwareInfoPollingInterval = ReadTimeSpan(section, "HardwareInfoPollingInterval", defaults.HardwareInfoPollingInterval),
                AllowFanControlCommands = ReadBoolean(section, "AllowFanControlCommands", defaults.AllowFanControlCommands),
            };
        }, cancellationToken);
    }

    private static TimeSpan ReadTimeSpan(JsonObject section, string propertyName, TimeSpan fallback)
    {
        if (section[propertyName] is JsonValue value && value.TryGetValue(out string? text)
            && !string.IsNullOrWhiteSpace(text)
            && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static bool ReadBoolean(JsonObject section, string propertyName, bool fallback)
    {
        if (section[propertyName] is JsonValue value && value.TryGetValue(out bool parsed))
        {
            return parsed;
        }

        return fallback;
    }

    public Task WriteAsync(FrameworkServiceOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var root = await LoadRootObjectAsync(ct).ConfigureAwait(false);
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
            await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(JsonWriterOptions), ct).ConfigureAwait(false);
            File.Move(temporaryPath, PersistentConfigurationPath, overwrite: true);

            _logger.LogInformation(
                "Persisted service configuration overlay to {PersistentConfigurationPath}. PollingInterval={PollingInterval}, HardwareInfoPollingInterval={HardwareInfoPollingInterval}, AllowFanControlCommands={AllowFanControlCommands}.",
                PersistentConfigurationPath,
                options.PollingInterval,
                options.HardwareInfoPollingInterval,
                options.AllowFanControlCommands);
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writeQueue.Dispose();
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
