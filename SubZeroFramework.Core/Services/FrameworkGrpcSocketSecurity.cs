namespace SubZeroFramework.Services;

/// <summary>
/// Provides shared validation and preparation rules for the local gRPC Unix domain socket endpoint.
/// </summary>
public static class FrameworkGrpcSocketSecurity
{
    /// <summary>
    /// Validates the local gRPC socket endpoint metadata and returns the outcome.
    /// </summary>
    /// <param name="socketPath">The socket path to validate.</param>
    public static FrameworkGrpcEndpointValidationResult ValidateEndpoint(string socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            throw new ArgumentException("Socket path cannot be null or whitespace.", nameof(socketPath));
        }

        var fullPath = Path.GetFullPath(socketPath);
        var expectedPath = Path.GetFullPath(FrameworkGrpcSocketPath.GetPath());
        var expectedDirectoryPath = Path.GetDirectoryName(expectedPath)
            ?? throw new InvalidOperationException("The expected socket path must include a parent directory.");
        var actualDirectoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The socket path must include a parent directory.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(expectedPath, fullPath, comparison))
        {
            return new FrameworkGrpcEndpointValidationResult
            {
                IsValid = false,
                FullPath = fullPath,
                Message = "The gRPC endpoint path does not match the expected local socket path.",
            };
        }

        if (!string.Equals(Path.GetFullPath(expectedDirectoryPath), Path.GetFullPath(actualDirectoryPath), comparison))
        {
            return new FrameworkGrpcEndpointValidationResult
            {
                IsValid = false,
                FullPath = fullPath,
                Message = "The gRPC endpoint directory does not match the expected local socket directory.",
            };
        }

        var directoryValidationMessage = ValidateDirectoryPath(actualDirectoryPath);
        if (directoryValidationMessage is not null)
        {
            return new FrameworkGrpcEndpointValidationResult
            {
                IsValid = false,
                FullPath = fullPath,
                Message = directoryValidationMessage,
            };
        }

        if (File.Exists(fullPath) && new FileInfo(fullPath).LinkTarget is not null)
        {
            return new FrameworkGrpcEndpointValidationResult
            {
                IsValid = false,
                FullPath = fullPath,
                Message = "The gRPC endpoint path cannot be a symbolic link.",
            };
        }

        if (!OperatingSystem.IsLinux() || !File.Exists(fullPath))
        {
            return new FrameworkGrpcEndpointValidationResult
            {
                IsValid = true,
                FullPath = fullPath,
                Message = "The gRPC endpoint path passed validation.",
            };
        }

        try
        {
            var fileMode = File.GetUnixFileMode(fullPath);
            if ((fileMode & (UnixFileMode.OtherWrite | UnixFileMode.GroupWrite)) != 0)
            {
                return new FrameworkGrpcEndpointValidationResult
                {
                    IsValid = false,
                    FullPath = fullPath,
                    Message = "The gRPC endpoint is writable by group or others.",
                };
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new FrameworkGrpcEndpointValidationResult
            {
                IsValid = false,
                FullPath = fullPath,
                Message = "The gRPC endpoint permissions could not be inspected.",
            };
        }
        catch (IOException)
        {
            return new FrameworkGrpcEndpointValidationResult
            {
                IsValid = false,
                FullPath = fullPath,
                Message = "The gRPC endpoint permissions could not be read.",
            };
        }

        return new FrameworkGrpcEndpointValidationResult
        {
            IsValid = true,
            FullPath = fullPath,
            Message = "The gRPC endpoint path passed validation.",
        };
    }

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

        var directoryValidationMessage = ValidateDirectoryPath(directoryPath);
        if (directoryValidationMessage is not null)
        {
            throw new InvalidOperationException(directoryValidationMessage);
        }

        Directory.CreateDirectory(directoryPath);

        if (OperatingSystem.IsWindows())
        {
            TryHardenWindowsDirectoryAttributes(directoryPath);
        }

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
        var validationResult = ValidateEndpoint(socketPath);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException(validationResult.Message);
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

    private static string? ValidateDirectoryPath(string directoryPath)
    {
        var currentDirectory = new DirectoryInfo(Path.GetFullPath(directoryPath));

        while (currentDirectory is not null)
        {
            if (currentDirectory.LinkTarget is not null)
            {
                return "The gRPC socket directory cannot be a symbolic link.";
            }

            if (OperatingSystem.IsWindows() && currentDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return "The gRPC socket directory cannot traverse a Windows reparse point.";
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }

    private static void TryHardenWindowsDirectoryAttributes(string directoryPath)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            var sanitizedAttributes = directoryInfo.Attributes & ~(FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.Temporary);

            if (sanitizedAttributes != directoryInfo.Attributes)
            {
                directoryInfo.Attributes = sanitizedAttributes;
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }
}
