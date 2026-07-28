#if WINDOWS10_0_26100_0_OR_GREATER
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

using Vanara.PInvoke;

using static Vanara.PInvoke.Msi;

namespace SubZeroFramework.Services;

/// <summary>
/// Finds this application's own Windows Installer package and hands it to msiexec for an interactive uninstall.
/// </summary>
/// <remarks>
/// The in-app uninstall used to delete the SCM entry directly, which is the entry the MSI declaratively owns —
/// so uninstalling in-app and then removing the app through Add/Remove Programs left Windows Installer trying
/// to remove a service that was already gone. Running the real uninstaller instead makes one component
/// responsible for removal, and the package already stops and deregisters the service itself
/// (<c>ServiceControl Remove="uninstall" Stop="both" Wait="yes"</c>), so the service must NOT be torn down
/// separately first — doing so would leave a machine installed-but-serviceless if the user then cancels.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsApplicationUninstaller
{
    /// <summary>
    /// Mirrors <c>Package/@UpgradeCode</c> in <c>packaging/windows/subzeroframework.wxs</c>.
    /// </summary>
    /// <remarks>
    /// The package authors no ProductCode, so WiX mints a fresh one on every build — two builds of the same
    /// version have different ProductCodes, and a major upgrade replaces it again. The UpgradeCode is the only
    /// stable identity, which is why the product is located through it rather than by a hard-coded ProductCode
    /// or by scanning the Uninstall registry keys for a display name.
    /// <see cref="SubZeroFramework.Tests"/> pins this against the .wxs so the two cannot drift silently.
    /// </remarks>
    internal const string UpgradeCode = "{7E4B8C21-5A9D-4F3E-9B0A-2C6D1E8F4A58}";

    /// <summary>38 GUID characters plus the terminating NUL, as MsiEnumRelatedProducts requires.</summary>
    private const int ProductCodeBufferLength = 39;

    /// <summary>ShellExecute reports a declined UAC prompt as ERROR_CANCELLED.</summary>
    internal const int ElevationCancelledErrorCode = 1223;

    /// <summary>
    /// The ProductCode of the installed package, or null when this copy did not come from the installer —
    /// a development build, an extracted archive, or a per-user context this process cannot see.
    /// </summary>
    public static string? TryFindInstalledProductCode()
    {
        // More than one product can be registered against an UpgradeCode (a broken partial install alongside
        // a good one), so the enumeration continues until an actually-installed one is found rather than
        // trusting the first index.
        for (uint index = 0; ; index++)
        {
            var buffer = new StringBuilder(ProductCodeBufferLength);
            if (MsiEnumRelatedProducts(UpgradeCode, 0, index, buffer).Failed)
            {
                // ERROR_NO_MORE_ITEMS ends the enumeration; anything else means nothing usable is registered.
                return null;
            }

            var productCode = buffer.ToString();
            if (MsiQueryProductState(productCode) == INSTALLSTATE.INSTALLSTATE_DEFAULT)
            {
                return productCode;
            }
        }
    }

    /// <summary>
    /// Starts the interactive uninstall and returns as soon as it is running.
    /// </summary>
    /// <remarks>
    /// The caller MUST exit the process immediately afterwards. While this app is alive its own executable is
    /// mapped, so Windows Installer would either raise its "files in use" dialog or defer the deletion to a
    /// reboot; quitting first is what makes the removal clean. Never wait for msiexec — it cannot finish
    /// until this process is gone, so waiting would deadlock the two against each other.
    /// </remarks>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// <see cref="ElevationCancelledErrorCode"/> when the user dismissed the elevation prompt.
    /// </exception>
    public static void StartInteractiveUninstall(string productCode)
    {
        // Absolute path rather than a bare file name: ShellExecute would otherwise resolve against the
        // working directory and PATH.
        var msiexecPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");

        var startInfo = new ProcessStartInfo
        {
            FileName = msiexecPath,
            // No quiet switch — the visible uninstall UI is the point. This is the same form Windows itself
            // stores in the package's uninstall string.
            Arguments = $"/x{productCode}",
            UseShellExecute = true,
            // Elevate up front so a refusal surfaces here, while the app can still report it, rather than
            // after the app has already quit. Deliberately NOT hidden, unlike the service-control helpers.
            Verb = "runas",
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Installer could not be started to uninstall SubZero.");
    }
}
#endif
