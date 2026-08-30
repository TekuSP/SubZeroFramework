using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Round-trips a fan's Adaptive state through the persisted file.
/// </summary>
/// <remarks>
/// The file is HAND-serialized on the write side but bound by the standard configuration binder on the read
/// side, so the two halves agree only because the JSON property names match the record property names. That
/// is exactly the kind of coupling that breaks silently — a renamed property still writes and still loads,
/// it just quietly loses the value. Hence a real write-then-read.
/// </remarks>
[TestFixture]
public class AdaptivePersistenceTests
{
    [Test]
    public async Task PersistedCalibration_SurvivesAWriteAndReadCycle()
    {
        var filePath = CreateTemporaryPath();
        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            var options = new FanControlStateOptions
            {
                FanIndex = 1,
                Mode = FanControlMode.Adaptive,
                Calibration = new FanCalibrationOptions
                {
                    State = FanCalibrationState.Ok,
                    CalibratedAt = new DateTimeOffset(2026, 8, 24, 10, 30, 0, TimeSpan.Zero),
                    ProcessGainCelsiusPerPercent = 0.42d,
                    TimeConstantSeconds = 26d,
                    DeadTimeSeconds = 4d,
                    MinimumSpinRpm = 1_180d,
                    MinimumSpinDutyPercent = 17d,
                    MaximumRpm = 7_000d,
                    ProportionalGain = 2.06d,
                    IntegralGain = 0.079d,
                    FeedForwardDutyPerWatt = 0.9d,
                },
                AdaptiveSettings = new AdaptiveFanSettingsOptions
                {
                    TargetTemperatureCelsius = 82d,
                    SafetyFloorEnabled = true,
                    SafetyFloorPercent = 22d,
                },
                AdaptiveLearning = new AdaptiveLearningOptions
                {
                    FeedForwardDutyPerWatt = 1.05d,
                    CalibratedAnchorDutyPerWatt = 0.9d,
                    IdentifiedProcessGainCelsiusPerPercent = 0.37d,
                    IdentifiedCelsiusPerWatt = 1.08d,
                    IdentifiedInterceptCelsius = 34.2d,
                    ObservationCount = 37,
                    LastUpdatedAt = new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                    LastMaterialChangeAt = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero),
                    ThermalLoadSource = ThermalLoadSource.System,
                },
            };

            await store.UpsertFanControlStateAsync(options, CancellationToken.None);

            var reloaded = ReadFanControlState(filePath, fanIndex: 1);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Mode, Is.EqualTo(FanControlMode.Adaptive));

                Assert.That(reloaded.Calibration, Is.Not.Null, "The calibration is what costs the user minutes of a loaded machine — losing it is the whole point of persisting.");
                Assert.That(reloaded.Calibration!.State, Is.EqualTo(FanCalibrationState.Ok));
                Assert.That(reloaded.Calibration.ProcessGainCelsiusPerPercent, Is.EqualTo(0.42d).Within(1e-9d));
                Assert.That(reloaded.Calibration.TimeConstantSeconds, Is.EqualTo(26d).Within(1e-9d));
                Assert.That(reloaded.Calibration.DeadTimeSeconds, Is.EqualTo(4d).Within(1e-9d));
                Assert.That(reloaded.Calibration.MinimumSpinRpm, Is.EqualTo(1_180d).Within(1e-9d));
                Assert.That(reloaded.Calibration.MinimumSpinDutyPercent, Is.EqualTo(17d).Within(1e-9d));
                Assert.That(reloaded.Calibration.MaximumRpm, Is.EqualTo(7_000d).Within(1e-9d));
                Assert.That(reloaded.Calibration.ProportionalGain, Is.EqualTo(2.06d).Within(1e-9d));
                Assert.That(reloaded.Calibration.IntegralGain, Is.EqualTo(0.079d).Within(1e-9d));
                Assert.That(reloaded.Calibration.FeedForwardDutyPerWatt, Is.EqualTo(0.9d).Within(1e-9d));
                Assert.That(reloaded.Calibration.CalibratedAt, Is.EqualTo(new DateTimeOffset(2026, 8, 24, 10, 30, 0, TimeSpan.Zero)));

                Assert.That(reloaded.AdaptiveSettings, Is.Not.Null);
                Assert.That(reloaded.AdaptiveSettings!.TargetTemperatureCelsius, Is.EqualTo(82d).Within(1e-9d));
                Assert.That(reloaded.AdaptiveSettings.SafetyFloorEnabled, Is.True);
                Assert.That(reloaded.AdaptiveSettings.SafetyFloorPercent, Is.EqualTo(22d).Within(1e-9d));

                Assert.That(reloaded.AdaptiveLearning, Is.Not.Null, "Learning must survive a restart or the machine relearns from scratch every boot.");
                Assert.That(reloaded.AdaptiveLearning!.FeedForwardDutyPerWatt, Is.EqualTo(1.05d).Within(1e-9d));
                Assert.That(reloaded.AdaptiveLearning.CalibratedAnchorDutyPerWatt, Is.EqualTo(0.9d).Within(1e-9d));
                Assert.That(reloaded.AdaptiveLearning.ObservationCount, Is.EqualTo(37));

                // The identified plant, without which a restart relearns over days instead of resuming.
                Assert.That(reloaded.AdaptiveLearning.IdentifiedProcessGainCelsiusPerPercent, Is.EqualTo(0.37d).Within(1e-9d));
                Assert.That(reloaded.AdaptiveLearning.IdentifiedCelsiusPerWatt, Is.EqualTo(1.08d).Within(1e-9d));
                Assert.That(reloaded.AdaptiveLearning.IdentifiedInterceptCelsius, Is.EqualTo(34.2d).Within(1e-9d));
                Assert.That(
                    reloaded.AdaptiveLearning.LastMaterialChangeAt,
                    Is.EqualTo(new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero)),
                    "Without this the reported confidence resets to Converging on every restart.");

                Assert.That(
                    reloaded.AdaptiveLearning.ThermalLoadSource,
                    Is.EqualTo(ThermalLoadSource.System),
                    "Without this the capability window re-runs and could feed the stored fit a different signal.");
            });
        }
        finally
        {
            TryDeleteDirectory(filePath);
        }
    }

    [Test]
    public async Task AFanThatNeverMetAdaptive_WritesNoAdaptiveKeys()
    {
        // Every existing user's file must stay as compact as it was before the feature existed.
        var filePath = CreateTemporaryPath();
        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.UpsertFanControlStateAsync(
                new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Auto },
                CancellationToken.None);

            var entry = ReadFanControlEntry(filePath, fanIndex: 0);

            Assert.Multiple(() =>
            {
                Assert.That(entry.ContainsKey("Calibration"), Is.False);
                Assert.That(entry.ContainsKey("AdaptiveLearning"), Is.False);
            });
        }
        finally
        {
            TryDeleteDirectory(filePath);
        }
    }

    [Test]
    public async Task LearningWithoutAGain_IsNotPersisted()
    {
        // An observation count with no gain is not a refinement, and writing it would resume a learner that
        // reports confidence it cannot back with a value.
        var filePath = CreateTemporaryPath();
        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.UpsertFanControlStateAsync(
                new FanControlStateOptions
                {
                    FanIndex = 0,
                    Mode = FanControlMode.Adaptive,
                    AdaptiveLearning = new AdaptiveLearningOptions { FeedForwardDutyPerWatt = null, ObservationCount = 9 },
                },
                CancellationToken.None);

            Assert.That(ReadFanControlEntry(filePath, fanIndex: 0).ContainsKey("AdaptiveLearning"), Is.False);
        }
        finally
        {
            TryDeleteDirectory(filePath);
        }
    }

    /// <summary>Binds the persisted file exactly as the running service does, through IConfiguration.</summary>
    private static FanControlStateOptions ReadFanControlState(string filePath, int fanIndex)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(filePath, optional: false)
            .Build();

        var options = new FrameworkServiceOptions();
        configuration.GetSection("FrameworkService").Bind(options);

        var state = options.FanControlStates.SingleOrDefault(entry => entry.FanIndex == fanIndex);
        Assert.That(state, Is.Not.Null, $"No persisted entry for fan {fanIndex}.");
        return state!;
    }

    private static JsonObject ReadFanControlEntry(string filePath, int fanIndex)
    {
        var root = JsonNode.Parse(File.ReadAllText(filePath))!.AsObject();
        var array = root["FrameworkService"]!.AsObject()["FanControlStates"]!.AsArray();

        foreach (var node in array)
        {
            if (node is JsonObject entry
                && entry["FanIndex"] is JsonValue value
                && value.TryGetValue(out int index)
                && index == fanIndex)
            {
                return entry;
            }
        }

        Assert.Fail($"No persisted entry for fan {fanIndex}.");
        throw new InvalidOperationException();
    }

    private static string CreateTemporaryPath()
        => Path.Combine(Path.Combine(Path.GetTempPath(), $"szf-adaptive-{Guid.NewGuid():N}"), "service-settings.json");

    private static void TryDeleteDirectory(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a test over.
        }
    }
}
