using NUnit.Framework;

using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkServiceAutorunStateParserTests
{
    [Test]
    public void ParseWindowsScQcOutput_WhenAutoStartIsReported_ReturnsTrue()
    {
        const string output = "START_TYPE         : 2   AUTO_START";

        Assert.That(FrameworkServiceAutorunStateParser.ParseWindowsScQcOutput(output), Is.True);
    }

    [Test]
    public void ParseWindowsScQcOutput_WhenDemandStartIsReported_ReturnsFalse()
    {
        const string output = "START_TYPE         : 3   DEMAND_START";

        Assert.That(FrameworkServiceAutorunStateParser.ParseWindowsScQcOutput(output), Is.False);
    }

    [Test]
    public void ParseWindowsScQcOutput_WhenOutputIsUnknown_ReturnsNull()
    {
        Assert.That(FrameworkServiceAutorunStateParser.ParseWindowsScQcOutput("SERVICE_NAME: SubZeroFramework"), Is.Null);
    }

    [Test]
    public void ParseLinuxSystemctlIsEnabledOutput_WhenExitCodeIsZero_ReturnsTrue()
    {
        Assert.That(FrameworkServiceAutorunStateParser.ParseLinuxSystemctlIsEnabledOutput("enabled", 0), Is.True);
    }

    [Test]
    public void ParseLinuxSystemctlIsEnabledOutput_WhenKnownDisabledStateIsReported_ReturnsFalse()
    {
        Assert.That(FrameworkServiceAutorunStateParser.ParseLinuxSystemctlIsEnabledOutput("masked", 1), Is.False);
    }

    [Test]
    public void ParseLinuxSystemctlIsEnabledOutput_WhenStateIsUnexpected_ReturnsNull()
    {
        Assert.That(FrameworkServiceAutorunStateParser.ParseLinuxSystemctlIsEnabledOutput("mystery", 42), Is.Null);
    }
}