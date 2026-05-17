namespace SubZeroFramework.Tests;

using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Principal;

using NUnit.Framework;

using SubZeroFramework.Service.Services;

[TestFixture]
public class FrameworkServiceManagementCliTests
{
    [Test]
    [SupportedOSPlatform("windows")]
    public void IsRunningAsAdministrator_WhenOnWindows_MatchesCurrentPrincipalState()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("This test validates the Windows branch.");
        }

        Assert.That(InvokeIsRunningAsAdministrator(), Is.EqualTo(GetExpectedIsAdministrator()));
    }

    [Test]
    [SupportedOSPlatform("windows")]
    public void EnsureManagementPrivileges_WhenOnWindows_UsesClearAdministratorMessage()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("This test validates the Windows branch.");
        }

        var exception = InvokeEnsureManagementPrivileges("update");

        if (GetExpectedIsAdministrator())
        {
            Assert.That(exception, Is.Null);
            return;
        }

        Assert.That(exception, Is.TypeOf<InvalidOperationException>());
        Assert.That(exception!.Message, Is.EqualTo("Service management operation 'update' requires administrator privileges on Windows. Re-run the packaged service executable as administrator so the client can complete this request."));
    }

    private static bool InvokeIsRunningAsAdministrator()
    {
        var cliType = typeof(FrameworkFanControlAuthorizationService).Assembly.GetType("SubZeroFramework.Service.FrameworkServiceManagementCli", throwOnError: true)!;
        var method = cliType.GetMethod("IsRunningAsAdministrator", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, null)!;
    }

    private static Exception? InvokeEnsureManagementPrivileges(string operation)
    {
        var cliType = typeof(FrameworkFanControlAuthorizationService).Assembly.GetType("SubZeroFramework.Service.FrameworkServiceManagementCli", throwOnError: true)!;
        var method = cliType.GetMethod("EnsureManagementPrivileges", BindingFlags.Static | BindingFlags.NonPublic)!;

        try
        {
            method.Invoke(null, [operation]);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool GetExpectedIsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity is null)
        {
            return false;
        }

        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}