namespace SubZeroFramework.Tests;

using System.Reflection;

using NUnit.Framework;

using SubZeroFramework.Models;

[TestFixture]
public class LinuxPrivilegeDetectorTests
{
    [Test]
    public void IsRunningAsRoot_WhenNotLinux_ReturnsFalse()
    {
        if (OperatingSystem.IsLinux())
        {
            Assert.Ignore("This test validates the non-Linux branch.");
        }

        Assert.That(InvokeIsRunningAsRoot(), Is.False);
    }

    private static bool InvokeIsRunningAsRoot()
    {
        var detectorType = typeof(FrameworkSystemStatus).Assembly.GetType("SubZeroFramework.Services.LinuxPrivilegeDetector", throwOnError: true)!;
        var method = detectorType.GetMethod("IsRunningAsRoot", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(null, null)!;
    }
}
