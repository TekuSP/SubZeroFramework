using System.Text.RegularExpressions;

using NUnit.Framework;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the MSI UpgradeCode the in-app uninstall uses against the one the installer is actually built with.
/// </summary>
/// <remarks>
/// The in-app uninstall finds the installed product by UpgradeCode, because the package authors no
/// ProductCode and WiX therefore mints a fresh one on every build. That makes the GUID a second source of
/// truth living in two files, and a mismatch fails in the worst possible way: silently. The uninstall button
/// would simply decide the app "was not installed by the installer" and quietly fall back to removing only
/// the service — on a machine where the MSI owns that service entry.
///
/// This compares the two files as text rather than referencing the app assembly, because the test project
/// deliberately references only Core and Service (the Uno app head is multi-targeted and not testable here).
/// </remarks>
[TestFixture]
public class WindowsInstallerIdentityTests
{
    [Test]
    public void UninstallerUpgradeCode_MatchesTheInstallerPackage()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            Assert.Ignore("Test is running outside a repository checkout, so the source files are unavailable.");
        }

        var wxsPath = Path.Combine(repositoryRoot!, "packaging", "windows", "subzeroframework.wxs");
        var uninstallerPath = Path.Combine(repositoryRoot!, "SubZeroFramework", "Services", "WindowsApplicationUninstaller.cs");

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(wxsPath), Is.True, $"Installer definition not found at {wxsPath}");
            Assert.That(File.Exists(uninstallerPath), Is.True, $"Uninstaller source not found at {uninstallerPath}");
        });

        var packagedUpgradeCode = Regex.Match(File.ReadAllText(wxsPath), """UpgradeCode\s*=\s*"([^"]+)""").Groups[1].Value;
        var uninstallerUpgradeCode = Regex.Match(File.ReadAllText(uninstallerPath), """UpgradeCode\s*=\s*"\{?([^"}]+)\}?"\s*;""").Groups[1].Value;

        Assert.Multiple(() =>
        {
            Assert.That(packagedUpgradeCode, Is.Not.Empty, "Could not read UpgradeCode from the .wxs");
            Assert.That(uninstallerUpgradeCode, Is.Not.Empty, "Could not read UpgradeCode from WindowsApplicationUninstaller");
        });

        // The .wxs writes it bare and lowercase; the C# constant is braced and upper-cased for the MSI API.
        // Compare as GUIDs so only the identity matters, not the formatting.
        Assert.Multiple(() =>
        {
            Assert.That(Guid.TryParse(packagedUpgradeCode, out var packaged), Is.True);
            Assert.That(Guid.TryParse(uninstallerUpgradeCode, out var uninstaller), Is.True);
            Assert.That(uninstaller, Is.EqualTo(packaged), "The in-app uninstall would not find the installed product.");
        });
    }

    [Test]
    public void InstallerPackage_StillAuthorsNoProductCode()
    {
        // The whole UpgradeCode approach exists because the ProductCode is regenerated per build. If someone
        // pins an explicit ProductCode later, the lookup could be simplified — and this test is the reminder.
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            Assert.Ignore("Test is running outside a repository checkout.");
        }

        var wxs = File.ReadAllText(Path.Combine(repositoryRoot!, "packaging", "windows", "subzeroframework.wxs"));

        Assert.That(
            Regex.IsMatch(wxs, """<Package[^>]*\bProductCode\s*="""),
            Is.False,
            "The package now authors a ProductCode; revisit WindowsApplicationUninstaller, which assumes it is generated per build.");
    }

    /// <summary>Walks up from the test assembly to the checkout root, identified by the solution file.</summary>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.sln").Any() || directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
