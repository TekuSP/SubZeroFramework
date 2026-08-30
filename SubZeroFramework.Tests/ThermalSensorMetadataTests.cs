using FrameworkDotnet.Enums;

using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// The firmware's own view of a temperature sensor — what it calls it, and where it acts on it.
/// </summary>
[TestFixture]
public class ThermalSensorMetadataTests
{
    /// <summary>
    /// The firmware name is the one that matches the service manual and the one a user searching for their
    /// machine will find, so it outranks the library's mapped enum.
    /// </summary>
    [Test]
    public void DisplayName_PrefersTheFirmwareName()
    {
        var metadata = new ThermalSensorMetadata { SensorIndex = 3, FirmwareName = "APU_SoC", MappedName = FrameworkSensorName.Generic };

        Assert.That(metadata.DisplayName, Is.EqualTo("APU_SoC"));
    }

    [Test]
    public void DisplayName_WithoutAFirmwareName_UsesTheMappedName()
    {
        var metadata = new ThermalSensorMetadata { SensorIndex = 1, MappedName = FrameworkSensorName.F75303Cpu };

        Assert.That(metadata.DisplayName, Is.EqualTo(nameof(FrameworkSensorName.F75303Cpu)));
    }

    /// <summary>
    /// A position is not a name, and saying "Temp 2" is more honest than inventing one.
    /// </summary>
    [Test]
    public void DisplayName_WithNeitherName_FallsBackToThePosition()
    {
        var metadata = new ThermalSensorMetadata { SensorIndex = 2 };

        Assert.That(metadata.DisplayName, Is.EqualTo("Temp 2"));
    }

    /// <summary>
    /// "Generic" is the library saying it does not know what this sensor is. Rendering that word as a name
    /// would be worse than the position, which is at least true.
    /// </summary>
    [Test]
    public void DisplayName_TreatsGenericAsNoNameAtAll()
    {
        var metadata = new ThermalSensorMetadata { SensorIndex = 4, MappedName = FrameworkSensorName.Generic };

        Assert.That(metadata.DisplayName, Is.EqualTo("Temp 4"));
    }

    /// <summary>Whitespace is not a name either — some firmware pads its strings.</summary>
    [Test]
    public void DisplayName_TreatsAWhitespaceFirmwareNameAsAbsent()
    {
        var metadata = new ThermalSensorMetadata { SensorIndex = 0, FirmwareName = "   ", MappedName = FrameworkSensorName.Battery };

        Assert.That(metadata.DisplayName, Is.EqualTo(nameof(FrameworkSensorName.Battery)));
    }

    [Test]
    public void HasThresholds_IsFalseWhenTheFirmwareReportedNone()
        => Assert.That(new ThermalSensorMetadata { SensorIndex = 0 }.HasThresholds, Is.False);

    [Test]
    public void HasThresholds_IsTrueWhenAnySingleThresholdIsPresent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ThermalSensorMetadata { SensorIndex = 0, WarnCelsius = 90d }.HasThresholds, Is.True);
            Assert.That(new ThermalSensorMetadata { SensorIndex = 0, HaltCelsius = 105d }.HasThresholds, Is.True);
            Assert.That(new ThermalSensorMetadata { SensorIndex = 0, FanMaxCelsius = 80d }.HasThresholds, Is.True);
        });
    }
}
