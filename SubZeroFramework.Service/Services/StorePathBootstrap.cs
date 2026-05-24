namespace SubZeroFramework.Service.Services;

/// <summary>
/// Persists the active location of a service-owned JSON store as a small pointer file
/// alongside the default path so the service can find a relocated store on next start
/// without needing any external configuration.
/// </summary>
public static class StorePathBootstrap
{
    public static string ResolveActivePath(string defaultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);

        var pointerPath = GetPointerPath(defaultPath);
        if (!File.Exists(pointerPath))
        {
            return Path.GetFullPath(defaultPath);
        }

        try
        {
            var pointed = File.ReadAllText(pointerPath).Trim();
            if (string.IsNullOrWhiteSpace(pointed))
            {
                return Path.GetFullPath(defaultPath);
            }

            return Path.GetFullPath(pointed);
        }
        catch (IOException)
        {
            return Path.GetFullPath(defaultPath);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.GetFullPath(defaultPath);
        }
    }

    public static void WritePointer(string defaultPath, string activePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(activePath);

        var pointerPath = GetPointerPath(defaultPath);
        var pointerDirectory = Path.GetDirectoryName(pointerPath);
        if (!string.IsNullOrWhiteSpace(pointerDirectory))
        {
            Directory.CreateDirectory(pointerDirectory);
        }

        File.WriteAllText(pointerPath, Path.GetFullPath(activePath));
    }

    public static void ClearPointer(string defaultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);

        var pointerPath = GetPointerPath(defaultPath);
        if (File.Exists(pointerPath))
        {
            File.Delete(pointerPath);
        }
    }

    public static string GetPointerPath(string defaultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);

        var directory = Path.GetDirectoryName(defaultPath);
        var fileName = Path.GetFileName(defaultPath);
        var pointerName = $"{fileName}.path";

        return string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(AppContext.BaseDirectory, pointerName)
            : Path.Combine(directory, pointerName);
    }
}
