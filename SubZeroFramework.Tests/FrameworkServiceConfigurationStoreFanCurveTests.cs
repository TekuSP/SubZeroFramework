using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkServiceConfigurationStoreFanCurveTests
{
    [Test]
    public async Task UpsertFanControlStateAsync_AddsEntryAndPreservesScalars()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, """
                {
                  "FrameworkService": {
                    "PollingInterval": "00:00:00.150",
                    "AllowFanControlCommands": true
                  }
                }
                """);

            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.UpsertFanControlStateAsync(new FanControlStateOptions
            {
                FanIndex = 1,
                Mode = FanControlMode.CustomCurve,
                CustomCurvePoints = new Dictionary<int, double> { [30] = 25d, [60] = 75d, [80] = 100d },
                DrivingTemperatureAggregation = TemperatureAggregationMode.Maximum,
                DrivingSensorIndices = [2, 5],
            });

            var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject();
            var section = root["FrameworkService"]!.AsObject();
            var array = section["FanControlStates"]!.AsArray();

            Assert.Multiple(() =>
            {
                Assert.That(section["AllowFanControlCommands"]!.GetValue<bool>(), Is.True);
                Assert.That(array.Count, Is.EqualTo(1));
                var entry = array[0]!.AsObject();
                Assert.That(entry["FanIndex"]!.GetValue<int>(), Is.EqualTo(1));
                Assert.That(entry["Mode"]!.GetValue<string>(), Is.EqualTo(nameof(FanControlMode.CustomCurve)));
                Assert.That(entry["DrivingTemperatureAggregation"]!.GetValue<string>(), Is.EqualTo(nameof(TemperatureAggregationMode.Maximum)));
                var points = entry["CustomCurvePoints"]!.AsObject();
                Assert.That(points["30"]!.GetValue<double>(), Is.EqualTo(25d));
                Assert.That(points["60"]!.GetValue<double>(), Is.EqualTo(75d));
                Assert.That(points["80"]!.GetValue<double>(), Is.EqualTo(100d));
                var sensors = entry["DrivingSensorIndices"]!.AsArray();
                Assert.That(sensors.Count, Is.EqualTo(2));
                Assert.That(sensors[0]!.GetValue<int>(), Is.EqualTo(2));
                Assert.That(sensors[1]!.GetValue<int>(), Is.EqualTo(5));
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task UpsertFanControlStateAsync_ReplacesExistingEntryWithSameFanIndex()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.UpsertFanControlStateAsync(new FanControlStateOptions
            {
                FanIndex = 0,
                Mode = FanControlMode.CustomCurve,
                CustomCurvePoints = new Dictionary<int, double> { [30] = 20d, [70] = 70d },
                DrivingTemperatureAggregation = TemperatureAggregationMode.Average,
                DrivingSensorIndices = [1],
            });

            await store.UpsertFanControlStateAsync(new FanControlStateOptions
            {
                FanIndex = 0,
                Mode = FanControlMode.CustomCurve,
                CustomCurvePoints = new Dictionary<int, double> { [40] = 40d, [80] = 90d },
                DrivingTemperatureAggregation = TemperatureAggregationMode.Maximum,
                DrivingSensorIndices = [3, 4],
            });

            var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject();
            var array = root["FrameworkService"]!.AsObject()["FanControlStates"]!.AsArray();

            Assert.That(array.Count, Is.EqualTo(1));
            var entry = array[0]!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(entry["DrivingTemperatureAggregation"]!.GetValue<string>(), Is.EqualTo(nameof(TemperatureAggregationMode.Maximum)));
                var points = entry["CustomCurvePoints"]!.AsObject();
                Assert.That(points.Count, Is.EqualTo(2));
                Assert.That(points["40"]!.GetValue<double>(), Is.EqualTo(40d));
                Assert.That(points["80"]!.GetValue<double>(), Is.EqualTo(90d));
                var sensors = entry["DrivingSensorIndices"]!.AsArray();
                Assert.That(sensors.Count, Is.EqualTo(2));
                Assert.That(sensors[0]!.GetValue<int>(), Is.EqualTo(3));
                Assert.That(sensors[1]!.GetValue<int>(), Is.EqualTo(4));
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task RemoveFanControlStateAsync_RemovesEntryAndReportsTrue()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.UpsertFanControlStateAsync(new FanControlStateOptions
            {
                FanIndex = 0,
                Mode = FanControlMode.CustomCurve,
                CustomCurvePoints = new Dictionary<int, double> { [30] = 20d, [80] = 90d },
                DrivingSensorIndices = [1],
            });

            await store.UpsertFanControlStateAsync(new FanControlStateOptions
            {
                FanIndex = 1,
                Mode = FanControlMode.CustomCurve,
                CustomCurvePoints = new Dictionary<int, double> { [40] = 30d, [85] = 100d },
                DrivingSensorIndices = [2],
            });

            var removed = await store.RemoveFanControlStateAsync(0);
            Assert.That(removed, Is.True);

            var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject();
            var array = root["FrameworkService"]!.AsObject()["FanControlStates"]!.AsArray();

            Assert.That(array.Count, Is.EqualTo(1));
            Assert.That(array[0]!.AsObject()["FanIndex"]!.GetValue<int>(), Is.EqualTo(1));
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task RemoveFanControlStateAsync_WhenNoMatchingEntry_ReturnsFalse()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.UpsertFanControlStateAsync(new FanControlStateOptions
            {
                FanIndex = 1,
                Mode = FanControlMode.CustomCurve,
                CustomCurvePoints = new Dictionary<int, double> { [30] = 20d, [80] = 90d },
                DrivingSensorIndices = [],
            });

            var removed = await store.RemoveFanControlStateAsync(99);
            Assert.That(removed, Is.False);
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task RemoveFanControlStateAsync_WhenNoFileExists_ReturnsFalse()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            var removed = await store.RemoveFanControlStateAsync(0);
            Assert.That(removed, Is.False);
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

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
