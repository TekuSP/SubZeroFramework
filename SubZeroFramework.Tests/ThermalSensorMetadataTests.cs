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
    /// The firmware name names the actual sensor, so it outranks the library's mapped enum — but it is
    /// rendered as a phrase rather than reproduced verbatim, underscores and all.
    /// </summary>
    [Test]
    public void DisplayName_PrefersTheFirmwareName()
    {
        var metadata = new ThermalSensorMetadata { SensorIndex = 3, FirmwareName = "APU_SoC", MappedName = FrameworkSensorName.Generic };

        Assert.That(metadata.DisplayName, Is.EqualTo("APU SOC"));
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

    /// <summary>
    /// The real names off a Framework 16. Firmware reports wiring — subject, sensor chip, I²C address — and
    /// only the subject belongs in a dashboard row.
    /// </summary>
    [TestCase("ambient_f75303@4d", ExpectedResult = "Ambient")]
    [TestCase("apu_f75303@4d", ExpectedResult = "APU")]
    [TestCase("gpu_amb_f75303@4d", ExpectedResult = "GPU ambient")]
    [TestCase("gpu_vram_f75303@4d", ExpectedResult = "GPU VRAM")]
    [TestCase("charger_f75303@4d", ExpectedResult = "Charger")]
    [TestCase("cpu@4c", ExpectedResult = "CPU")]
    [TestCase("gpu_vr_f75303@4d", ExpectedResult = "GPU VR")]
    [TestCase("gpu_temp@40", ExpectedResult = "GPU")]
    public string FriendlyFirmwareName_ReducesAFirmwareNameToItsSubject(string firmwareName)
        => new ThermalSensorMetadata { SensorIndex = 0, FirmwareName = firmwareName }.FriendlyFirmwareName;

    [Test]
    public void FriendlyFirmwareName_WithoutAFirmwareName_IsEmpty()
        => Assert.That(new ThermalSensorMetadata { SensorIndex = 0 }.FriendlyFirmwareName, Is.Empty);

    /// <summary>
    /// A name that is nothing BUT a part number leaves no subject behind. Reporting the empty string sends
    /// the caller to its own fallback rather than rendering a bare chip code.
    /// </summary>
    [Test]
    public void FriendlyFirmwareName_WhenOnlyAPartNumberRemains_IsEmpty()
        => Assert.That(new ThermalSensorMetadata { SensorIndex = 0, FirmwareName = "f75303@4d" }.FriendlyFirmwareName, Is.Empty);

    /// <summary>
    /// A lone "temp" IS the subject on a sensor with no other name, so the trailing-word rule must not strip
    /// it down to nothing.
    /// </summary>
    [Test]
    public void FriendlyFirmwareName_KeepsTempWhenItIsTheOnlyWord()
        => Assert.That(new ThermalSensorMetadata { SensorIndex = 0, FirmwareName = "temp@40" }.FriendlyFirmwareName, Is.EqualTo("Temp"));

    [Test]
    public void DisplayName_UsesTheFriendlyFirmwareName()
        => Assert.That(
            new ThermalSensorMetadata { SensorIndex = 3, FirmwareName = "apu_f75303@4d" }.DisplayName,
            Is.EqualTo("APU"));

    /// <summary>An unusable firmware name must fall through to the mapped name, not render as blank.</summary>
    [Test]
    public void DisplayName_WhenTheFirmwareNameReducesToNothing_FallsBackToTheMappedName()
        => Assert.That(
            new ThermalSensorMetadata { SensorIndex = 3, FirmwareName = "f75303@4d", MappedName = FrameworkSensorName.Battery }.DisplayName,
            Is.EqualTo(nameof(FrameworkSensorName.Battery)));

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
