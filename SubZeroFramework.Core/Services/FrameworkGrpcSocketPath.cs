namespace SubZeroFramework.Services;

public static class FrameworkGrpcSocketPath
{
    private const string SocketDirectoryName = "SubZeroFramework";
    private const string SocketFileName = "subzeroframework.grpc.sock";

    public static string GetPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var directoryPath = Path.Combine(Path.GetTempPath(), SocketDirectoryName);
            Directory.CreateDirectory(directoryPath);
            return Path.Combine(directoryPath, SocketFileName);
        }

        return Path.Combine("/run", SocketFileName);
    }
}
