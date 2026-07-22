using NUnit.Framework;

using Microsoft.Extensions.Logging.Abstractions;

using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkFanControlAuthorizationServiceTests
{
    [Test]
    public void EnsureCommandAccess_WhenCommandsAreDisabled_ThrowsInvalidOperationException()
    {
        FrameworkFanControlAuthorizationService service = CreateService(allowFanControlCommands: false);

        // The disabled message must tell the user HOW to enable fan control (the Settings toggle) and
        // must NOT cite IPC/caller-identity validation — that stale wording read as an unfixable
        // transport error and stopped the first Linux tester from enabling fan control at all.
        Assert.That(() => service.EnsureCommandAccess(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("Fan-control commands are switched off. Turn on \"Allow fan control commands\" under Settings → Service, then apply."));
    }

    [Test]
    public void GetAuthorizationMessage_WhenCommandsAreDisabled_PointsAtTheSettingsToggle()
    {
        FrameworkFanControlAuthorizationService service = CreateService(allowFanControlCommands: false);

        Assert.That(service.GetAuthorizationMessage(), Does.Contain("Settings → Service"));
        Assert.That(service.GetAuthorizationMessage(), Does.Not.Contain("caller identity"),
            "The disabled message must not present the opt-in as an IPC validation failure.");
    }

    [Test]
    public void GetAuthorizationMessage_WhenCommandsAreEnabled_ReportsEnabledForLocalClients()
    {
        FrameworkFanControlAuthorizationService service = CreateService(allowFanControlCommands: true);

        Assert.That(service.GetAuthorizationMessage(), Does.StartWith("Fan-control RPCs are enabled for"));
        Assert.That(service.GetAuthorizationMessage(), Does.Not.Contain("caller identity"),
            "The enabled message must read as a working state, not a warning.");
    }

    private static FrameworkFanControlAuthorizationService CreateService(bool allowFanControlCommands)
        => new(new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            AllowFanControlCommands = allowFanControlCommands,
        }), NullLogger<FrameworkFanControlAuthorizationService>.Instance);
}
