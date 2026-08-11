namespace SubZeroFramework.Service.Services;

/// <summary>
/// Shared implementation of the "relocate service-owned JSON store" flow. Validates the target
/// directory is writable by the service account, copies the existing file to the new location,
/// updates the bootstrap pointer (or clears it when reverting to the default directory),
/// and best-effort deletes the previous file. Used by both the configuration overlay and the
/// machine-wide user-preferences stores so there is a single relocation flow on the service.
/// </summary>
public static class StorePathRelocator
{
    public static async Task<StoreRelocationResult> RelocateAsync(
        string currentPath,
        string defaultPath,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new StoreRelocationResult(false, "A target directory must be selected before relocating the store.", currentPath);
        }

        string targetDirectoryAbsolute;
        try
        {
            targetDirectoryAbsolute = Path.GetFullPath(targetDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new StoreRelocationResult(false, $"The target directory is not a valid path. {exception.Message}", currentPath);
        }

        try
        {
            Directory.CreateDirectory(targetDirectoryAbsolute);
        }
        catch (Exception exception)
        {
            return new StoreRelocationResult(false, $"Failed to create the target directory '{targetDirectoryAbsolute}'. The service account may not have permission. {exception.Message}", currentPath);
        }

        // The service writes this file as root (LocalSystem on Windows), and the path comes straight from a
        // client over a socket that any local account can connect to. Relocating into a directory that other
        // accounts can write to would hand them the service's own configuration — which is loaded and applied
        // on the next start, fan curves and all — so refuse those targets rather than trusting the caller.
        // The socket path already gets this treatment; the store path did not.
        if (DescribeUnsafeTargetDirectory(targetDirectoryAbsolute) is { } unsafeReason)
        {
            return new StoreRelocationResult(false, unsafeReason, currentPath);
        }

        var fileName = Path.GetFileName(defaultPath);
        var targetPath = Path.GetFullPath(Path.Combine(targetDirectoryAbsolute, fileName));
        var currentPathAbsolute = Path.GetFullPath(currentPath);
        var defaultDirectory = Path.GetDirectoryName(Path.GetFullPath(defaultPath)) ?? AppContext.BaseDirectory;
        var targetIsDefault = string.Equals(
            Path.GetFullPath(targetDirectoryAbsolute).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(defaultDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        if (string.Equals(targetPath, currentPathAbsolute, StringComparison.OrdinalIgnoreCase))
        {
            return new StoreRelocationResult(true, $"The store is already located at '{targetPath}'. No change applied.", currentPathAbsolute);
        }

        try
        {
            var probePath = Path.Combine(targetDirectoryAbsolute, $".{fileName}.relocate-probe-{Guid.NewGuid():N}");
            await File.WriteAllBytesAsync(probePath, [], cancellationToken).ConfigureAwait(false);
            File.Delete(probePath);
        }
        catch (Exception exception)
        {
            return new StoreRelocationResult(false, $"The service account cannot write to '{targetDirectoryAbsolute}'. {exception.Message}", currentPathAbsolute);
        }

        try
        {
            if (File.Exists(currentPathAbsolute))
            {
                // CreateNew, and an unpredictable name. The previous form opened a PREDICTABLE path
                // ("<target>/service-settings.json.tmp") with FileMode.Create, which follows symlinks on
                // Linux and hardlinks on Windows — anyone able to create that one name in the target
                // directory could point it at a file the root service may write and have it truncated and
                // overwritten. FileMode.CreateNew maps to O_CREAT|O_EXCL, which refuses to follow a symlink
                // even when its target does not exist, so a planted link fails the relocation instead of
                // redirecting it; the GUID removes the guessable name that made planting practical.
                var temporaryPath = Path.Combine(targetDirectoryAbsolute, $".{fileName}.{Guid.NewGuid():N}.tmp");
                await using (var source = new FileStream(currentPathAbsolute, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, targetPath, overwrite: true);
            }
        }
        catch (Exception exception)
        {
            return new StoreRelocationResult(false, $"Failed to copy '{currentPathAbsolute}' to '{targetPath}'. {exception.Message}", currentPathAbsolute);
        }

        try
        {
            if (targetIsDefault)
            {
                StorePathBootstrap.ClearPointer(defaultPath);
            }
            else
            {
                StorePathBootstrap.WritePointer(defaultPath, targetPath);
            }
        }
        catch (Exception exception)
        {
            return new StoreRelocationResult(false, $"Failed to persist the bootstrap pointer for '{defaultPath}'. The relocated copy at '{targetPath}' will not be used after the service restarts. {exception.Message}", currentPathAbsolute);
        }

        try
        {
            if (File.Exists(currentPathAbsolute)
                && !string.Equals(currentPathAbsolute, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(currentPathAbsolute);
            }
        }
        catch
        {
            // Best-effort delete; the pointer already references the new location.
        }

        var summary = targetIsDefault
            ? $"Restored the store to the default location '{targetPath}'."
            : $"Relocated the store to '{targetPath}'.";

        return new StoreRelocationResult(true, summary, targetPath);
    }

    /// <summary>
    /// Returns a user-facing reason when the target is somewhere other local accounts could tamper with the
    /// relocated store, or null when it is safe to use.
    /// </summary>
    /// <remarks>
    /// Checks the directory AND its parent: a directory that is itself 0755 is still attacker-controlled if
    /// its parent is world-writable, because it can simply be renamed out of the way and replaced.
    ///
    /// Linux only. Windows has no comparable one-line check — the equivalent is an ACL review, and the
    /// service runs as LocalSystem where %ProgramData% ACL inheritance is its own (separately tracked)
    /// problem. This deliberately does not silently pass on Windows so much as have nothing correct to say
    /// there yet.
    /// </remarks>
    private static string? DescribeUnsafeTargetDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        foreach (var candidate in new[] { directory, Path.GetDirectoryName(directory) })
        {
            if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
            {
                continue;
            }

            UnixFileMode mode;
            try
            {
                mode = File.GetUnixFileMode(candidate);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Unreadable mode is not proof of danger, and refusing every target we cannot stat would
                // break relocation on filesystems that do not carry Unix modes at all.
                continue;
            }

            if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                return $"'{candidate}' is writable by other local accounts, so the service will not place its "
                    + "configuration there. Choose a directory owned by root with no group or world write "
                    + "permission (for example under /var/lib).";
            }
        }

        return null;
    }
}
