using System.Runtime.InteropServices;

namespace SubZeroFramework.Services.Compute;

/// <summary>
/// Loads a vendor GPU library without letting the OS pick which file that is.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NativeLibrary.TryLoad(string, out IntPtr)"/> with a BARE module name asks the Windows loader to
/// search, and that search ends at <c>%PATH%</c>. These libraries are loaded inside a service running as
/// LocalSystem, so an unprivileged user who can write to any directory on the machine PATH — a depressingly
/// common condition on developer machines — could plant <c>nvml.dll</c>, <c>amdadlx64.dll</c> or
/// <c>ControlLib.dll</c> there and have it loaded, and its <c>DllMain</c> executed, as SYSTEM on the next
/// service start.
/// </para>
/// <para>
/// So on Windows a bare name is resolved to exactly one trusted location — System32, which is where the
/// drivers install these and where the callers' own comments already say they expect them — and loaded from
/// that absolute path or not at all. There is deliberately NO fallback to the bare name: a library that is
/// not in System32 is one this process cannot prove the provenance of, and every caller already treats "not
/// loadable" as an ordinary, handled outcome (the telemetry source is simply reported unavailable).
/// </para>
/// <para>
/// Non-Windows keeps <c>dlopen</c>'s own resolution: the Linux candidates are sonames rather than bare
/// filenames, <c>ld.so</c> does not search <c>PATH</c>, and the privileged-service threat this guards against
/// is a Windows one.
/// </para>
/// </remarks>
internal static class TrustedNativeLibrary
{
    /// <summary>Loads <paramref name="moduleName"/> from a trusted location.</summary>
    /// <param name="moduleName">A bare module name, an soname, or an absolute path.</param>
    /// <param name="handle">The module handle when this returns true.</param>
    /// <returns>True when the library was loaded.</returns>
    public static bool TryLoad(string moduleName, out IntPtr handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        // An absolute path is the caller naming the file outright; there is no search to constrain.
        if (Path.IsPathRooted(moduleName) || !OperatingSystem.IsWindows())
        {
            return NativeLibrary.TryLoad(moduleName, out handle);
        }

        var systemDirectory = Environment.SystemDirectory;
        if (string.IsNullOrWhiteSpace(systemDirectory))
        {
            handle = IntPtr.Zero;
            return false;
        }

        return NativeLibrary.TryLoad(Path.Combine(systemDirectory, moduleName), out handle);
    }
}
