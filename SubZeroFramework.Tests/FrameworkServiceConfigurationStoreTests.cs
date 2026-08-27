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
                SecondaryPollingInterval = TimeSpan.FromMilliseconds(750),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(2),
                AllowFanControlCommands = true,
            });

            var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath))!.AsObject();
            var frameworkService = root["FrameworkService"]!.AsObject();

            Assert.Multiple(() =>
            {
                Assert.That(root["Existing"]!["Value"]!.GetValue<int>(), Is.EqualTo(42));
                Assert.That(frameworkService["PollingInterval"]!.GetValue<string>(), Is.EqualTo(TimeSpan.FromMilliseconds(250).ToString("c", CultureInfo.InvariantCulture)));
                Assert.That(frameworkService["SecondaryPollingInterval"]!.GetValue<string>(), Is.EqualTo(TimeSpan.FromMilliseconds(750).ToString("c", CultureInfo.InvariantCulture)));
                Assert.That(frameworkService["HardwareInfoPollingInterval"]!.GetValue<string>(), Is.EqualTo(TimeSpan.FromSeconds(2).ToString("c", CultureInfo.InvariantCulture)));
                Assert.That(frameworkService["AllowFanControlCommands"]!.GetValue<bool>(), Is.True);
            });
        }
        finally
        {
            DeleteTemporaryPath(filePath);
        }
    }

    /// <summary>
    /// Every polling tier must survive a write/read cycle.
    /// </summary>
    /// <remarks>
    /// Writing and reading are separate lists of property names, so a tier added to one and forgotten in the
    /// other silently resets to its default on the next service start — the setting appears to save, and then
    /// quietly does not. A round trip is the only assertion that catches that; testing the write alone does
    /// not, which is how the gap would have gone unnoticed.
    /// </remarks>
    [Test]
    public async Task WriteThenReadAsync_PreservesEveryPollingTier()
    {
        var filePath = CreateTemporaryPath();

        try
        {
            using var store = new FrameworkServiceConfigurationStore(filePath, NullLogger<FrameworkServiceConfigurationStore>.Instance);

            // Deliberately none of the defaults, so a tier that silently falls back is visible.
            var written = new FrameworkServiceOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(175),
                SecondaryPollingInterval = TimeSpan.FromMilliseconds(1250),
                HardwareInfoPollingInterval = TimeSpan.FromSeconds(45),
                AllowFanControlCommands = true,
            };

            await store.WriteAsync(written);
            var read = await store.ReadAsync();

            Assert.That(read, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(read!.PollingInterval, Is.EqualTo(written.PollingInterval), "Primary tier.");
                Assert.That(read.SecondaryPollingInterval, Is.EqualTo(written.SecondaryPollingInterval), "Secondary tier.");
                Assert.That(read.HardwareInfoPollingInterval, Is.EqualTo(written.HardwareInfoPollingInterval), "Tertiary tier.");
                Assert.That(read.AllowFanControlCommands, Is.True);
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