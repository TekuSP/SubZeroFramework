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

    public Task UpsertFanControlStateAsync(FanControlStateOptions state, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var root = await LoadRootObjectAsync(ct).ConfigureAwait(false);
            var section = root["FrameworkService"] as JsonObject ?? new JsonObject();
            var array = section["FanControlStates"] as JsonArray ?? new JsonArray();

            var existingIndex = -1;
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonObject entry
                    && entry["FanIndex"] is JsonValue value
                    && value.TryGetValue(out int existingFanIndex)
                    && existingFanIndex == state.FanIndex)
                {
                    existingIndex = i;
                    break;
                }
            }

            var node = SerializeFanControlState(state);
            if (existingIndex >= 0)
            {
                array[existingIndex] = node;
            }
            else
            {
                array.Add(node);
            }

            section["FanControlStates"] = array;
            root["FrameworkService"] = section;

            await PersistRootAsync(root, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Persisted fan control state for fan {FanIndex} with {ProfileCount} curve profile(s), active slot {ActiveCurveSlot}, to {PersistentConfigurationPath}.",
                state.FanIndex,
                state.CurveProfiles.Length,
                state.ActiveCurveSlot,
                PersistentConfigurationPath);
        }, cancellationToken);
    }

    public Task<bool> RemoveFanControlStateAsync(int fanIndex, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var root = await LoadRootObjectAsync(ct).ConfigureAwait(false);
            if (root["FrameworkService"] is not JsonObject section
                || section["FanControlStates"] is not JsonArray array)
            {
                return false;
            }

            var removed = false;
            for (var i = array.Count - 1; i >= 0; i--)
            {
                if (array[i] is JsonObject entry
                    && entry["FanIndex"] is JsonValue value
                    && value.TryGetValue(out int existingFanIndex)
                    && existingFanIndex == fanIndex)
                {
                    array.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                return false;
            }

            section["FanControlStates"] = array;
            root["FrameworkService"] = section;

            await PersistRootAsync(root, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Removed persisted fan control state for fan {FanIndex} from {PersistentConfigurationPath}.",
                fanIndex,
                PersistentConfigurationPath);

            return true;
        }, cancellationToken);
    }

    private static JsonObject SerializeFanControlState(FanControlStateOptions state)
    {
        var node = new JsonObject
        {
            ["FanIndex"] = state.FanIndex,
            ["Mode"] = state.Mode.ToString(),
            ["ActiveCurveSlot"] = state.ActiveCurveSlot,
        };

        var profiles = new JsonArray();
        foreach (var profile in state.CurveProfiles.OrderBy(static p => p.Slot))
        {
            profiles.Add(SerializeCurveProfile(profile));
        }
        node["CurveProfiles"] = profiles;

        if (state.LinkedLeaderIndex is int linkedLeaderIndex)
        {
            node["LinkedLeaderIndex"] = linkedLeaderIndex;
        }

        if (state.CpuUsageModifierStrength is double cpuUsageModifierStrength && double.IsFinite(cpuUsageModifierStrength))
        {
            node["CpuUsageModifierStrength"] = cpuUsageModifierStrength;
        }

        return node;
    }

    private static JsonObject SerializeCurveProfile(FanCurveProfileOptions profile)
    {
        var node = new JsonObject
        {
            ["Slot"] = profile.Slot,
            ["DrivingTemperatureAggregation"] = profile.DrivingTemperatureAggregation.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(profile.Name))
        {
            node["Name"] = profile.Name;
        }

        var pointsObject = new JsonObject();
        foreach (var pair in profile.CurvePoints.OrderBy(static p => p.Key))
        {
            pointsObject[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value;
        }
        node["CurvePoints"] = pointsObject;

        var sensors = new JsonArray();
        foreach (var sensorIndex in profile.DrivingSensorIndices)
        {
            sensors.Add(sensorIndex);
        }
        node["DrivingSensorIndices"] = sensors;

        if (profile.FollowFanIndex is int followFanIndex)
        {
            node["FollowFanIndex"] = followFanIndex;
        }

        return node;
    }

    private async Task PersistRootAsync(JsonObject root, CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(PersistentConfigurationPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var temporaryPath = $"{PersistentConfigurationPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(JsonWriterOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, PersistentConfigurationPath, overwrite: true);
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

            await PersistRootAsync(root, ct).ConfigureAwait(false);

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
