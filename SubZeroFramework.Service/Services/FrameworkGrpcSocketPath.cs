namespace SubZeroFramework.Service.Services;

internal static class FrameworkGrpcSocketPath
{
    private const string SocketDirectoryName = "SubZeroFramework";
    private const string SocketSubdirectoryName = "ipc";
    private const string SocketFileName = "subzeroframework.grpc.sock";

    public static string GetPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var commonApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var directoryPath = Path.Combine(commonApplicationDataPath, SocketDirectoryName, SocketSubdirectoryName);
            Directory.CreateDirectory(directoryPath);
            return Path.Combine(directoryPath, SocketFileName);
        }

        return Path.Combine("/run", SocketFileName);
    }
}
