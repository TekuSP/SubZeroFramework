namespace SubZeroFramework.Service.Services;

/// <summary>
/// Result of a <see cref="StorePathRelocator"/> relocation request.
/// <see cref="ActivePath"/> always reflects the path the store should use after the call,
/// regardless of whether the relocation succeeded or rolled back.
/// </summary>
public sealed record StoreRelocationResult(bool Succeeded, string Message, string ActivePath);

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
                var temporaryPath = $"{targetPath}.tmp";
                await using (var source = new FileStream(currentPathAbsolute, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
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
}
