using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using SubZeroFramework.Models;
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

        ProbeWritability();
    }

    /// <summary>
    /// Proves at startup that the configuration path can actually be written, and logs a WARNING when it
    /// cannot — one loud line at second zero instead of a quiet failure on every apply. A service run
    /// without write access (a dev build launched un-elevated against a ProgramData folder the installed
    /// service created as SYSTEM) otherwise persists nothing while every command reports success, and the
    /// user discovers it as "my applied mode randomly reverts on restart".
    /// </summary>
    private void ProbeWritability()
    {
        var probePath = _persistentConfigurationPath + ".probe";
        try
        {
            var directory = Path.GetDirectoryName(probePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The persistent configuration path {PersistentConfigurationPath} is NOT writable by this process. "
                + "Nothing applied in this session will survive a service restart. "
                + "Grant this account write access to the folder, or run the service with sufficient rights.",
                _persistentConfigurationPath);
        }
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
                SecondaryPollingInterval = ReadTimeSpan(section, "SecondaryPollingInterval", defaults.SecondaryPollingInterval),
                HardwareInfoPollingInterval = ReadTimeSpan(section, "HardwareInfoPollingInterval", defaults.HardwareInfoPollingInterval),
                // Written by WriteAsync below; read here so a chosen retention actually comes back. Neither
                // half existed, so a saved retention was accepted, clamped, applied live — and silently
                // discarded at the next service start.
                PrimaryRetention = ReadTimeSpan(section, "PrimaryRetention", defaults.PrimaryRetention),
                SecondaryRetention = ReadTimeSpan(section, "SecondaryRetention", defaults.SecondaryRetention),
                TertiaryRetention = ReadTimeSpan(section, "TertiaryRetention", defaults.TertiaryRetention),
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

    /// <summary>
    /// Removes every persisted per-fan control state in a single write — including orphan entries for fan
    /// indices the hardware no longer reports, which a loop over live fans can never reach. Scalar service
    /// settings (polling intervals, the fan-control permission) are left untouched. Returns how many entries
    /// were removed; writes nothing when there were none, so a reset does not pointlessly retrigger the
    /// configuration reload.
    /// </summary>
    public Task<int> ClearAllFanControlStatesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var root = await LoadRootObjectAsync(ct).ConfigureAwait(false);
            if (root["FrameworkService"] is not JsonObject section
                || section["FanControlStates"] is not JsonArray array)
            {
                return 0;
            }

            var removedCount = array.Count;

            // Drop the key entirely rather than leaving an empty array, so the file matches a fresh install.
            section.Remove("FanControlStates");
            root["FrameworkService"] = section;

            await PersistRootAsync(root, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Cleared {RemovedCount} persisted fan control state entry(ies) from {PersistentConfigurationPath} for a factory reset.",
                removedCount,
                PersistentConfigurationPath);

            return removedCount;
        }, cancellationToken);
    }

    private static JsonObject SerializeFanControlState(FanControlStateOptions state)
    {
        var node = new JsonObject
        {
            ["FanIndex"] = state.FanIndex,
            ["Mode"] = state.Mode.ToString(),
            // The live top-level driving fields. For an ADAPTIVE fan these are the only record of the
            // sensors the loop holds — the store put them in the options, but this hand-written serializer
            // dropped them on the floor, so a restart restored Adaptive with no sensors and the fan fell
            // back to Auto.
            ["DrivingTemperatureAggregation"] = state.DrivingTemperatureAggregation.ToString(),
            ["ActiveCurveSlot"] = state.ActiveCurveSlot,
        };

        var drivingSensors = new JsonArray();
        foreach (var sensorIndex in state.DrivingSensorIndices)
        {
            drivingSensors.Add(sensorIndex);
        }
        node["DrivingSensorIndices"] = drivingSensors;

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

        // Each of the three below is written only when present, so a fan that never met Adaptive keeps the
        // same compact entry it had before the feature existed.
        if (state.Calibration is { } calibration)
        {
            node["Calibration"] = SerializeCalibration(calibration);
        }

        if (state.AdaptiveSettings is { } adaptiveSettings)
        {
            node["AdaptiveSettings"] = new JsonObject
            {
                ["TargetTemperatureCelsius"] = adaptiveSettings.TargetTemperatureCelsius,
                ["SafetyFloorEnabled"] = adaptiveSettings.SafetyFloorEnabled,
                ["SafetyFloorPercent"] = adaptiveSettings.SafetyFloorPercent,
                ["LambdaSeconds"] = adaptiveSettings.LambdaSeconds,
            };
        }

        if (state.AdaptiveLearning is { FeedForwardDutyPerWatt: double learnedGain } learning)
        {
            var learningNode = new JsonObject
            {
                ["FeedForwardDutyPerWatt"] = learnedGain,
                ["ObservationCount"] = learning.ObservationCount,
            };

            if (learning.CalibratedAnchorDutyPerWatt is double anchor)
            {
                learningNode["CalibratedAnchorDutyPerWatt"] = anchor;
            }

            if (learning.LastUpdatedAt is DateTimeOffset lastUpdatedAt)
            {
                learningNode["LastUpdatedAt"] = lastUpdatedAt.ToString("O", CultureInfo.InvariantCulture);
            }

            if (learning.LastMaterialChangeAt is DateTimeOffset lastMaterialChangeAt)
            {
                learningNode["LastMaterialChangeAt"] = lastMaterialChangeAt.ToString("O", CultureInfo.InvariantCulture);
            }

            // The identified plant, so a restart resumes the fit instead of relearning it over days.
            if (learning.IdentifiedProcessGainCelsiusPerPercent is double identifiedGain)
            {
                learningNode["IdentifiedProcessGainCelsiusPerPercent"] = identifiedGain;
            }

            if (learning.IdentifiedCelsiusPerWatt is double identifiedResistance)
            {
                learningNode["IdentifiedCelsiusPerWatt"] = identifiedResistance;
            }

            if (learning.IdentifiedInterceptCelsius is double identifiedIntercept)
            {
                learningNode["IdentifiedInterceptCelsius"] = identifiedIntercept;
            }

            // Without this the capability window re-runs on every restart and could settle differently,
            // leaving the fit above being fed samples that mean something else.
            if (learning.ThermalLoadSource != ThermalLoadSource.None)
            {
                learningNode["ThermalLoadSource"] = learning.ThermalLoadSource.ToString();
            }

            node["AdaptiveLearning"] = learningNode;
        }

        return node;
    }

    private static JsonObject SerializeCalibration(FanCalibrationOptions calibration)
    {
        var node = new JsonObject
        {
            ["State"] = calibration.State.ToString(),
            ["ProcessGainCelsiusPerPercent"] = calibration.ProcessGainCelsiusPerPercent,
            ["TimeConstantSeconds"] = calibration.TimeConstantSeconds,
            ["DeadTimeSeconds"] = calibration.DeadTimeSeconds,
            ["MinimumSpinRpm"] = calibration.MinimumSpinRpm,
            ["MinimumSpinDutyPercent"] = calibration.MinimumSpinDutyPercent,
            ["MaximumRpm"] = calibration.MaximumRpm,
            ["ProportionalGain"] = calibration.ProportionalGain,
            ["IntegralGain"] = calibration.IntegralGain,
            ["FeedForwardDutyPerWatt"] = calibration.FeedForwardDutyPerWatt,
            ["TrackingMode"] = calibration.TrackingMode.ToString(),
        };

        if (calibration.CalibratedAt is DateTimeOffset calibratedAt)
        {
            node["CalibratedAt"] = calibratedAt.ToString("O", CultureInfo.InvariantCulture);
        }

        // The gain curve is what makes gain scheduling possible, and the control loop reads it — so it has to
        // survive a restart or the loop silently falls back to one averaged gain.
        if (calibration.GainCurvePoints is { Length: > 0 } gainCurvePoints)
        {
            var points = new JsonArray();
            foreach (var point in gainCurvePoints)
            {
                points.Add(new JsonObject
                {
                    ["DutyPercent"] = point.DutyPercent,
                    ["SettledCelsius"] = point.SettledCelsius,
                });
            }

            node["GainCurvePoints"] = points;
        }

        if (calibration.PerformanceResponse is { } performanceResponse)
        {
            var responseNode = new JsonObject
            {
                ["LowDutyPercent"] = performanceResponse.LowDutyPercent,
                ["FullDutyPercent"] = performanceResponse.FullDutyPercent,
            };

            // Each reading is written only when it was actually taken, so an absent one stays absent rather
            // than coming back as a measured zero.
            if (performanceResponse.CpuPerformanceRatioAtLowDuty is double cpuLow)
            {
                responseNode["CpuPerformanceRatioAtLowDuty"] = cpuLow;
            }

            if (performanceResponse.CpuPerformanceRatioAtFullDuty is double cpuFull)
            {
                responseNode["CpuPerformanceRatioAtFullDuty"] = cpuFull;
            }

            if (performanceResponse.GpuCoreClockAtLowDutyMegahertz is double gpuLow)
            {
                responseNode["GpuCoreClockAtLowDutyMegahertz"] = gpuLow;
            }

            if (performanceResponse.GpuCoreClockAtFullDutyMegahertz is double gpuFull)
            {
                responseNode["GpuCoreClockAtFullDutyMegahertz"] = gpuFull;
            }

            node["PerformanceResponse"] = responseNode;
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

        // Only written when set, so an untouched profile keeps its existing on-disk shape.
        if (profile.TreatMissingSensorsAsZero)
        {
            node["TreatMissingSensorsAsZero"] = true;
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
            frameworkServiceSection["SecondaryPollingInterval"] = options.SecondaryPollingInterval.ToString("c", CultureInfo.InvariantCulture);
            frameworkServiceSection["HardwareInfoPollingInterval"] = options.HardwareInfoPollingInterval.ToString("c", CultureInfo.InvariantCulture);
            frameworkServiceSection["PrimaryRetention"] = options.PrimaryRetention.ToString("c", CultureInfo.InvariantCulture);
            frameworkServiceSection["SecondaryRetention"] = options.SecondaryRetention.ToString("c", CultureInfo.InvariantCulture);
            frameworkServiceSection["TertiaryRetention"] = options.TertiaryRetention.ToString("c", CultureInfo.InvariantCulture);
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
