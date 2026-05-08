using NUnit.Framework;

using SubZeroFramework.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Tests;

[TestFixture]
public class FrameworkGrpcSocketSecurityTests
{
    [Test]
    public void ValidateEndpoint_WhenPathDoesNotMatchExpected_ReturnsInvalidResult()
    {
        string invalidPath = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), "Unexpected", "subzeroframework.grpc.sock")
            : "/tmp/subzeroframework.grpc.sock";

        FrameworkGrpcEndpointValidationResult result = FrameworkGrpcSocketSecurity.ValidateEndpoint(invalidPath);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Message, Is.EqualTo("The gRPC endpoint path does not match the expected local socket path."));
    }

    [Test]
    public void ValidateExpectedClientSocketPath_WhenPathDoesNotMatchExpected_ThrowsInvalidOperationException()
    {
        string invalidPath = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), "Unexpected", "subzeroframework.grpc.sock")
            : "/tmp/subzeroframework.grpc.sock";

        Assert.That(() => FrameworkGrpcSocketSecurity.ValidateExpectedClientSocketPath(invalidPath),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("The gRPC endpoint path does not match the expected local socket path."));
    }
}
