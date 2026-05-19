using System.Globalization;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkServiceConfigurationStoreTests
{
    [Test]
    public async Task WriteAsync_PersistsFrameworkServiceSettingsAndPreservesOtherRootData()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, """
                {
                  "Existing": {
                    "Value": 42
                  }
                }
                """);

            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.WriteAsync(new FrameworkServiceOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(250),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(2),
                AllowFanControlCommands = true,
            });

            var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject();
            var frameworkService = root["FrameworkService"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(root["Existing"]!["Value"]!.GetValue<int>(), Is.EqualTo(42));
                Assert.That(frameworkService["PollingInterval"]!.GetValue<string>(), Is.EqualTo(TimeSpan.FromMilliseconds(250).ToString("c", CultureInfo.InvariantCulture)));
                Assert.That(frameworkService["HardwareInfoPollingInterval"]!.GetValue<string>(), Is.EqualTo(TimeSpan.FromSeconds(2).ToString("c", CultureInfo.InvariantCulture)));
                Assert.That(frameworkService["AllowFanControlCommands"]!.GetValue<bool>(), Is.True);
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    [Test]
    public async Task WriteAsync_WhenPathDoesNotExist_CreatesDirectoryAndConfigurationFile()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            await store.WriteAsync(new FrameworkServiceOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(500),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(3),
                AllowFanControlCommands = false,
            });

            Assert.That(File.Exists(filePath), Is.True);
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