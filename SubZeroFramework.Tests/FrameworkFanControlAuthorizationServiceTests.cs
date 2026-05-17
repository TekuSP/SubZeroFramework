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

        Assert.That(() => service.EnsureCommandAccess(),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("Fan-control RPCs are disabled by service configuration until local caller identity validation is available for this IPC transport."));
    }

    [Test]
    public void GetAuthorizationMessage_WhenCommandsAreEnabledWithoutCallerValidation_ReturnsTransportWarning()
    {
        FrameworkFanControlAuthorizationService service = CreateService(allowFanControlCommands: true);

        Assert.That(service.GetAuthorizationMessage(),
            Is.EqualTo("Fan-control RPCs are enabled by configuration, but this transport does not currently expose portable caller identity validation on the server."));
    }

    private static FrameworkFanControlAuthorizationService CreateService(bool allowFanControlCommands)
        => new(new TestOptionsMonitor<FrameworkServiceOptions>(new FrameworkServiceOptions
        {
            AllowFanControlCommands = allowFanControlCommands,
        }), NullLogger<FrameworkFanControlAuthorizationService>.Instance);
}
