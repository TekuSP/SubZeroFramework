namespace SubZeroFramework.Tests;

using NUnit.Framework;

using SubZeroFramework.Models;

[TestFixture]
public class FrameworkSystemStatusTests
{
    [Test]
    public void LastTelemetryObservedAt_DefaultsToMinValue()
    {
        FrameworkSystemStatus status = new();

        Assert.That(status.LastTelemetryObservedAt, Is.EqualTo(DateTimeOffset.MinValue));
    }
}
