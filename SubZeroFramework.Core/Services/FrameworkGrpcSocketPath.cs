namespace SubZeroFramework.Services;

public static class FrameworkGrpcSocketPath
{
    private const string SocketDirectoryName = "SubZeroFramework";
    private const string SocketSubdirectoryName = "ipc";
    private const string SocketFileName = "subzeroframework.grpc.sock";

    /// <summary>
    /// The machine-scoped path of the local gRPC socket. Computes the path only — it does NOT create it.
    /// </summary>
    /// <remarks>
    /// This used to call <c>Directory.CreateDirectory</c>, and that was the root of the Windows ACL problem.
    /// Both processes call this: the service (as LocalSystem) and the UNPRIVILEGED app. Whichever ran first
    /// created %ProgramData%\SubZeroFramework\ipc and, under the default %ProgramData% ACL, became its owner
    /// with CREATOR OWNER full control. On a machine where the app won that race the desktop user ended up
    /// owning the LocalSystem service's directory outright — observed in the wild, not theoretical.
    ///
    /// Creation belongs to the service alone, which already does it in
    /// <see cref="FrameworkGrpcSocketSecurity.PrepareServerSocketPath"/> before binding and hardens the ACL
    /// immediately afterwards. A client that finds no directory simply finds no socket, which is the correct
    /// reading of "the service is not running".
    /// </remarks>
    public static string GetPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var commonApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(commonApplicationDataPath, SocketDirectoryName, SocketSubdirectoryName, SocketFileName);
        }

        return Path.Combine("/run", SocketFileName);
    }
}
