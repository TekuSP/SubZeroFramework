namespace SubZeroFramework.Services;

/// <summary>
/// Provides shared validation and preparation rules for the local gRPC Unix domain socket endpoint.
/// </summary>
public static class FrameworkGrpcSocketSecurity
{
    /// <summary>
    /// Prepares the server socket path before Kestrel binds to it.
    /// </summary>
    /// <param name="socketPath">The expected socket path.</param>
    public static void PrepareServerSocketPath(string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            throw new ArgumentException("Socket path cannot be null or whitespace.", nameof(socketPath));
        }

        var fullSocketPath = Path.GetFullPath(socketPath);
        var directoryPath = Path.GetDirectoryName(fullSocketPath)
            ?? throw new InvalidOperationException("Socket path must include a parent directory.");

        Directory.CreateDirectory(directoryPath);

        if (File.Exists(fullSocketPath))
        {
            if (new FileInfo(fullSocketPath).LinkTarget is not null)
            {
                throw new InvalidOperationException("The gRPC socket path cannot be a symbolic link.");
            }

            File.Delete(fullSocketPath);
        }

        if (OperatingSystem.IsLinux())
        {
            TryHardenUnixDirectoryPermissions(directoryPath);
        }
    }

    /// <summary>
    /// Validates that the client is attempting to connect to the expected local socket path.
    /// </summary>
    /// <param name="socketPath">The candidate socket path.</param>
    public static void ValidateExpectedClientSocketPath(string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            throw new ArgumentException("Socket path cannot be null or whitespace.", nameof(socketPath));
        }

        var expectedPath = Path.GetFullPath(FrameworkGrpcSocketPath.GetPath());
        var actualPath = Path.GetFullPath(socketPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(expectedPath, actualPath, comparison))
        {
            throw new InvalidOperationException("The gRPC client attempted to connect to an unexpected socket path.");
        }

        if (File.Exists(actualPath) && new FileInfo(actualPath).LinkTarget is not null)
        {
            throw new InvalidOperationException("The gRPC client cannot connect through a symbolic link.");
        }
    }

    private static void TryHardenUnixDirectoryPermissions(string directoryPath)
    {
        try
        {
            var currentMode = File.GetUnixFileMode(directoryPath);
            var hardenedMode = currentMode & ~(UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);

            if (hardenedMode != currentMode)
            {
                File.SetUnixFileMode(directoryPath, hardenedMode);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
