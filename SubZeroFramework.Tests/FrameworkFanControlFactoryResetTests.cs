using System.Text.Json.Nodes;

using DynamicData;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

/// <summary>
/// Covers the two halves of "Reset fan settings to factory defaults": the in-memory wipe
/// (<see cref="FrameworkFanControlStateStore.ResetAllToFactoryDefaults"/>) and the persisted wipe
/// (<see cref="FrameworkServiceConfigurationStore.ClearAllFanControlStatesAsync"/>). Both must hold for the
/// reset to survive a service restart.
/// </summary>
[TestFixture]
public class FrameworkFanControlFactoryResetTests
{
    [Test]
    public void ResetAllToFactoryDefaults_ClearsModeProfilesAndLink()
    {
        using var store = CreateStore();

        store.SaveCurveProfile(
            1,
            slot: 2,
            name: "Quiet",
            curvePoints: new Dictionary<int, double> { [40] = 30d, [80] = 100d },
            aggregationMode: TemperatureAggregationMode.Maximum,
            drivingSensorIndices: [0, 3],
            followFanIndex: null,
            activate: true);
        store.SetLinkedLeader(1, 0);
        store.MarkMax(3);

        store.ResetAllToFactoryDefaults();

        var resetFan = store.GetState(1);
        var maxedFan = store.GetState(3);

        Assert.Multiple(() =>
        {
            Assert.That(resetFan, Is.Not.Null);
            Assert.That(resetFan!.Mode, Is.EqualTo(FanControlMode.Auto));
            Assert.That(resetFan.ActiveCurveSlot, Is.EqualTo(0));
            Assert.That(resetFan.CurveProfiles.Any(static profile => profile.IsConfigured), Is.False, "Every curve profile slot must be emptied.");
            Assert.That(resetFan.LinkedLeaderIndex, Is.Null);
            Assert.That(resetFan.CustomCurvePoints, Is.Empty);
            Assert.That(resetFan.LastDutyPercent, Is.Null);

            Assert.That(maxedFan, Is.Not.Null);
            Assert.That(maxedFan!.Mode, Is.EqualTo(FanControlMode.Auto), "A Max override is a saved fan setting too.");
        });
    }

    [Test]
    public void ResetAllToFactoryDefaults_KeepsTheFansThemselves()
    {
        // The fan still exists — only its settings are gone. Removing the entries would reach clients as an
        // "unavailable" update that keeps the last known profiles, leaving stale slots on screen.
        using var store = CreateStore();
        store.MarkMax(0);
        store.MarkMax(1);

        var reset = store.ResetAllToFactoryDefaults();

        Assert.Multiple(() =>
        {
            Assert.That(reset, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(store.GetState(0), Is.Not.Null);
            Assert.That(store.GetState(1), Is.Not.Null);
        });
    }

    [Test]
    public void ResetAllToFactoryDefaults_PublishesAnUpdatePerFanAndNoRemoval()
    {
        using var store = CreateStore();
        store.MarkMax(0);
        store.MarkManual(1);

        var updates = 0;
        var removals = 0;
        using var subscription = store.Connect().Subscribe(changes =>
        {
            foreach (var change in changes)
            {
                if (change.Reason == ChangeReason.Remove)
                {
                    removals++;
                }
                else
                {
                    updates++;
                }
            }
        });

        updates = 0;
        removals = 0;

        store.ResetAllToFactoryDefaults();

        Assert.Multiple(() =>
        {
            Assert.That(updates, Is.EqualTo(2), "Each fan must publish its factory-default state to connected clients.");
            Assert.That(removals, Is.Zero, "A removal would reach clients as 'unavailable' and keep the stale profiles.");
        });
    }

    [Test]
    public void ClearAppliedDuty_ForgetsTheDutyButKeepsTheProfile()
    {
        // The firmware-safe fallback path: the service stops driving the fan, so the last duty it wrote is no
        // longer what the fan is running — but the user's profile must survive completely intact so the curve
        // resumes by itself when a driving sensor reports again.
        using var store = CreateStore();

        store.SaveCurveProfile(
            0,
            slot: 1,
            name: "Quiet",
            curvePoints: new Dictionary<int, double> { [40] = 30d, [80] = 100d },
            aggregationMode: TemperatureAggregationMode.Maximum,
            drivingSensorIndices: [4, 5],
            followFanIndex: null,
            activate: true,
            treatMissingSensorsAsZero: true);
        store.RecordAppliedDuty(0, 62d);

        store.ClearAppliedDuty(0);

        var state = store.GetState(0);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.LastDutyPercent, Is.Null, "A duty nobody is commanding must not be reported.");
            Assert.That(state.Mode, Is.EqualTo(FanControlMode.CustomCurve), "The fallback is not a mode change.");
            Assert.That(state.ActiveCurveSlot, Is.EqualTo(1));
            Assert.That(state.CurveProfiles[1].IsConfigured, Is.True);
            Assert.That(state.CurveProfiles[1].CurvePoints, Has.Count.EqualTo(2));
            Assert.That(state.CurveProfiles[1].TreatMissingSensorsAsZero, Is.True);
            Assert.That(state.DrivingSensorIndices, Is.EqualTo(new[] { 4, 5 }));
        });
    }

    [Test]
    public void ResetAllToFactoryDefaults_OnAnEmptyStore_ResetsNothing()
    {
        using var store = CreateStore();

        Assert.Multiple(() =>
        {
            Assert.That(store.ResetAllToFactoryDefaults(), Is.Empty);
            Assert.That(store.GetState(0), Is.Null, "The reset must not materialize fans that were never known.");
        });
    }

    [Test]
    public void BuildFanControlOptions_AfterReset_HasNothingLeftToPersist()
    {
        using var store = CreateStore();
        store.SaveCurveProfile(
            0,
            slot: 0,
            name: null,
            curvePoints: new Dictionary<int, double> { [50] = 60d },
            aggregationMode: TemperatureAggregationMode.Maximum,
            drivingSensorIndices: [1],
            followFanIndex: null,
            activate: true);

        store.ResetAllToFactoryDefaults();

        var options = store.BuildFanControlOptions(0);

        Assert.Multiple(() =>
        {
            Assert.That(options, Is.Not.Null);
            Assert.That(options!.Mode, Is.EqualTo(FanControlMode.Auto));
            Assert.That(options.CurveProfiles, Is.Empty);
            Assert.That(options.LinkedLeaderIndex, Is.Null);
        });
    }

    [Test]
    public async Task ClearAllFanControlStatesAsync_RemovesEveryEntryAndPreservesScalars()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, """
                {
                  "FrameworkService": {
                    "PollingInterval": "00:00:00.150",
                    "AllowFanControlCommands": true,
                    "FanControlStates": [
                      { "FanIndex": 0, "Mode": "Max", "ActiveCurveSlot": 0 },
                      { "FanIndex": 1, "Mode": "CustomCurve", "ActiveCurveSlot": 2 }
                    ]
                  }
                }
                """);

            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            var removed = await store.ClearAllFanControlStatesAsync();

            var section = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject()["FrameworkService"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.EqualTo(2));
                Assert.That(section["FanControlStates"], Is.Null, "The key is dropped entirely, matching a fresh install.");
                Assert.That(section["AllowFanControlCommands"]!.GetValue<bool>(), Is.True, "Scalar service settings are not fan settings.");
                Assert.That(section["PollingInterval"]!.GetValue<string>(), Is.EqualTo("00:00:00.150"));
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task ClearAllFanControlStatesAsync_RemovesOrphanEntriesForFansThatNoLongerEnumerate()
    {
        // The whole reason the wipe is one file operation rather than a loop over live fans: an entry for a
        // fan the hardware no longer reports is unreachable from the in-memory store.
        var filePath = CreateTemporaryPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, """
                {
                  "FrameworkService": {
                    "FanControlStates": [
                      { "FanIndex": 0, "Mode": "Max" },
                      { "FanIndex": 7, "Mode": "Manual" }
                    ]
                  }
                }
                """);

            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            var removed = await store.ClearAllFanControlStatesAsync();

            var section = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject()["FrameworkService"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.EqualTo(2));
                Assert.That(section["FanControlStates"], Is.Null);
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task ClearAllFanControlStatesAsync_WithNothingStored_WritesNoFile()
    {
        // Writing anyway would retrigger the configuration reload for no reason.
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            var removed = await store.ClearAllFanControlStatesAsync();

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.Zero);
                Assert.That(File.Exists(filePath), Is.False);
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task ClearAllFanControlStatesAsync_ThenUpsert_LeavesOnlyTheNewEntry()
    {
        // A per-fan persist that lands after the reset must not resurrect the cleared entries.
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.UpsertFanControlStateAsync(new FanControlStateOptions { FanIndex = 0, Mode = FanControlMode.Max });
            await store.UpsertFanControlStateAsync(new FanControlStateOptions { FanIndex = 1, Mode = FanControlMode.Max });

            var removed = await store.ClearAllFanControlStatesAsync();

            await store.UpsertFanControlStateAsync(new FanControlStateOptions { FanIndex = 2, Mode = FanControlMode.Manual });

            var array = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject()["FrameworkService"]!.AsObject()["FanControlStates"]!.AsArray();

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.EqualTo(2));
                Assert.That(array.Count, Is.EqualTo(1));
                Assert.That(array[0]!.AsObject()["FanIndex"]!.GetValue<int>(), Is.EqualTo(2));
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    private static FrameworkFanControlStateStore CreateStore()
        => new(
            new StubFrameworkDataProvider(),
            new FrameworkFanControlSafetyTracker(),
            new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions()),
            NullLogger<FrameworkFanControlStateStore>.Instance);

    private static string CreateTemporaryPath()
        => Path.Combine(Path.GetTempPath(), "SubZeroFramework.Tests", Guid.NewGuid().ToString("N"), "service-settings.json");

    private static void DeleteTemporaryPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
