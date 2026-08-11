using System.Security.AccessControl;
using System.Security.Principal;

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
            var directoryMode = GetUnixFileModeLinux(actualDirectoryPath);
            if ((directoryMode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                return new FrameworkGrpcEndpointValidationResult
                {
                    IsValid = false,
                    FullPath = fullPath,
                    Message = "The gRPC endpoint directory is writable by group or others.",
                };
            }

            if ((directoryMode & UnixFileMode.UserExecute) == 0)
            {
                return new FrameworkGrpcEndpointValidationResult
                {
                    IsValid = false,
                    FullPath = fullPath,
                    Message = "The gRPC endpoint directory is not accessible to its owner.",
                };
            }

            // NOTE: the socket FILE is deliberately allowed to be group/other-writable on Linux, and this
            // is load-bearing — connect(2) on a Unix domain socket requires WRITE permission on the socket
            // file. The service runs as root (EC access) while the app runs unprivileged, so a root-owned
            // 0755 socket is unreachable by the very client that must use it. Rejecting a writable socket
            // here therefore made the permission the client needs the same permission it refused to accept:
            // the app sat in "Background service offline" against a healthy, running service
            // (observed on Framework 13 / Arch, 2026-07-21).
            //
            // The protection that actually matters is kept above and is NOT relaxed: the DIRECTORY must not
            // be group/other-writable. A locked-down directory is what stops another user from unlinking or
            // replacing the socket with their own; permissions on the socket file itself only gate who may
            // open a channel, which is the same exposure the shipped posture already documents
            // (HasCallerIdentityValidation = false, machine-scoped path, fan-control RPCs fail-closed behind
            // AllowFanControlCommands). See SubZeroFramework/Docs/IpcAuthorizationAndUiCadence.md.
            var fileMode = GetUnixFileModeLinux(fullPath);
            if ((fileMode & (UnixFileMode.UserRead | UnixFileMode.UserWrite)) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            {
                return new FrameworkGrpcEndpointValidationResult
                {
                    IsValid = false,
                    FullPath = fullPath,
                    Message = "The gRPC endpoint owner does not have read/write access.",
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
            // The PARENT holds service-settings.json and user-preferences.json, and previously received no
            // hardening at all — only the ipc leaf did. Under the inherited %ProgramData% ACL that left the
            // desktop user owning the directory containing the LocalSystem service's own configuration, with
            // full control over it, which reaches the fan-control opt-in by simply editing the file. It is
            // hardened first and more tightly than the leaf: clients never need to write here, only to
            // traverse it on the way to the socket.
            var parentDirectoryPath = Path.GetDirectoryName(directoryPath);
            if (!string.IsNullOrEmpty(parentDirectoryPath))
            {
                TryHardenWindowsDirectoryAccessControl(parentDirectoryPath, FileSystemRights.ReadAndExecute);
            }

            TryHardenWindowsDirectory(directoryPath);
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

    /// <summary>
    /// Makes the bound Unix socket connectable by unprivileged local clients.
    /// </summary>
    /// <remarks>
    /// MUST be called AFTER the server has bound, because Kestrel creates the socket file itself during
    /// bind — <see cref="PrepareServerSocketPath"/> runs before that and can only prepare the directory.
    /// The socket inherits the server's umask (0022 under systemd), landing at 0755 root:root; since
    /// connect(2) needs WRITE permission, the unprivileged app could not reach its own service. Widening
    /// the socket to rw for everyone is what makes the local IPC usable at all when the service runs as
    /// root for EC access. The directory around it stays locked down, which is the control that prevents
    /// the socket being replaced. No-ops on non-Linux and never throws: failing to relax permissions must
    /// not take the service down, it only degrades to the same unreachable state as before.
    /// </remarks>
    /// <param name="socketPath">The bound socket path.</param>
    public static void AllowLocalClientsToConnect(string socketPath)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(socketPath))
        {
            return;
        }

        try
        {
            var fullSocketPath = Path.GetFullPath(socketPath);
            if (!File.Exists(fullSocketPath))
            {
                return;
            }

            SetUnixFileModeLinux(
                fullSocketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
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

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void TryHardenUnixDirectoryPermissions(string directoryPath)
    {
        try
        {
            var currentMode = GetUnixFileModeLinux(directoryPath);
            var hardenedMode = currentMode & ~(UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
            hardenedMode |= UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

            if (hardenedMode != currentMode)
            {
                SetUnixFileModeLinux(directoryPath, hardenedMode);
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

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static UnixFileMode GetUnixFileModeLinux(string path)
        => File.GetUnixFileMode(path);

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void SetUnixFileModeLinux(string path, UnixFileMode mode)
        => File.SetUnixFileMode(path, mode);

    private static string? ValidateDirectoryPath(string directoryPath)
    {
        var currentDirectory = new DirectoryInfo(Path.GetFullPath(directoryPath));

        while (currentDirectory is not null)
        {
            // A level that does not exist yet cannot be a link or a reparse point, and asking for its
            // Attributes THROWS. Now that GetPath no longer creates the directory (creation is the service's
            // job alone — see FrameworkGrpcSocketPath.GetPath), the client validates this path before the
            // service has ever run, so a missing level is the normal state on a machine where the service is
            // not installed, not an error.
            if (!currentDirectory.Exists)
            {
                currentDirectory = currentDirectory.Parent;
                continue;
            }

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

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TryHardenWindowsDirectory(string directoryPath)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            var sanitizedAttributes = directoryInfo.Attributes & ~(FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.Temporary);

            if (sanitizedAttributes != directoryInfo.Attributes)
            {
                directoryInfo.Attributes = sanitizedAttributes;
            }

            TryHardenWindowsDirectoryAccessControl(directoryPath);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TryHardenWindowsDirectoryAccessControl(
        string directoryPath,
        FileSystemRights interactiveUserRights = FileSystemRights.Modify | FileSystemRights.Synchronize)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            var directorySecurity = directoryInfo.GetAccessControl();
            var inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            const PropagationFlags propagationFlags = PropagationFlags.None;

            // preserveInheritance: FALSE. It used to be true, which does not mean "keep inheriting" — it
            // COPIES the inherited ACEs down as explicit ones and then protects them. Under the default
            // %ProgramData% ACL that includes CREATOR OWNER: whichever account created the directory keeps
            // full control over it, and the app runs unprivileged and calls GetPath() too, so a standard user
            // who gets there first ends up owning the LocalSystem service's directory permanently. Dropping
            // the inherited set and stating the ACL explicitly is the only way to be sure what is on it.
            directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            directorySecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow));
            directorySecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow));

            // Interactive users need this directory to reach the socket — the app is unprivileged and every
            // desktop account must be able to connect, so the grant is to Users rather than to whichever
            // account happened to run first. Modify, not FullControl: it is enough to open and use the
            // socket, while withholding the ownership and ACL-editing rights that CREATOR OWNER conferred.
            directorySecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                interactiveUserRights,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow));

            // DACL first, and ALONE. SetOwner must not share this call: SetAccessControl writes every section
            // the object has been given, so a caller that cannot change ownership fails the whole write and
            // loses the DACL with it. That is not hypothetical — setting the owner here in one pass silently
            // left both directories on their inherited ACEs, which is exactly the permissive state this
            // method exists to replace.
            directoryInfo.SetAccessControl(directorySecurity);

            // Ownership second, on its own object, so failure costs nothing already applied. It matters
            // because an owner can rewrite the ACL regardless of what it says — leaving the creating account
            // as owner would let it undo the hardening above. Only the elevated service can do this; the
            // unprivileged app leaves it, and the service corrects it on its next start.
            try
            {
                var ownerSecurity = directoryInfo.GetAccessControl();
                ownerSecurity.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                directoryInfo.SetAccessControl(ownerSecurity);
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or PrivilegeNotHeldException)
            {
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (SystemException)
        {
        }
    }
}
