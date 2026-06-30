using FrameworkDotnet.Enums;

using NUnit.Framework;

using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

public sealed class FrameworkSensorNameDisplayTests
{
    [TestCase(FrameworkSensorName.Apu, "APU / SoC")]
    [TestCase(FrameworkSensorName.F75303Cpu, "CPU")]
    [TestCase(FrameworkSensorName.Peci, "CPU (PECI)")]
    [TestCase(FrameworkSensorName.F75303Ddr, "Memory")]
    [TestCase(FrameworkSensorName.DgpuVram, "GPU VRAM")]
    [TestCase(FrameworkSensorName.Battery, "Battery")]
    [TestCase(FrameworkSensorName.F75303Skin, "Chassis")]
    [TestCase(FrameworkSensorName.F75303Amb, "Ambient")]
    [TestCase(FrameworkSensorName.Virtual, "Aggregate")]
    public void ToLocation_MapsKnownRoles(FrameworkSensorName name, string expected)
    {
        Assert.That(FrameworkSensorNameDisplay.ToLocation(name), Is.EqualTo(expected));
    }

    [TestCase(FrameworkSensorName.Unknown)]
    [TestCase(FrameworkSensorName.Generic)]
    [TestCase(null)]
    public void ToLocation_ReturnsNull_ForIndeterminateRoles(FrameworkSensorName? name)
    {
        Assert.That(FrameworkSensorNameDisplay.ToLocation(name), Is.Null);
    }

    [Test]
    public void ToLocation_CoversEveryEnumValue()
    {
        // Every defined role must resolve to either a label or an intentional null — no unmapped surprises.
        foreach (FrameworkSensorName name in System.Enum.GetValues<FrameworkSensorName>())
        {
            Assert.DoesNotThrow(() => FrameworkSensorNameDisplay.ToLocation(name));
        }
    }
}
