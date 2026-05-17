using System.Runtime.InteropServices;

namespace SubZeroFramework.Services;

internal static partial class ClientLinuxPrivilegeDetector
{
    public static bool IsRunningAsRoot()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        return GetEffectiveUserId() == 0;
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}