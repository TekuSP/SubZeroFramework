using System.Text.Json;
using System.Text.Json.Serialization;

using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkUserPreferencesStore : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly ReactiveRequestQueue _writeQueue = new();
    private readonly UnitPreferenceCatalog _catalog;
    private readonly ILogger<FrameworkUserPreferencesStore> _logger;
    private readonly string _defaultPreferencesPath;
    private string _preferencesPath;
    private bool _disposed;

    public FrameworkUserPreferencesStore(UnitPreferenceCatalog catalog, ILogger<FrameworkUserPreferencesStore> logger)
        : this(FrameworkServiceConfigurationPaths.GetUserPreferencesPath(), catalog, logger)
    {
    }

    public FrameworkUserPreferencesStore(string defaultPreferencesPath, UnitPreferenceCatalog catalog, ILogger<FrameworkUserPreferencesStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPreferencesPath);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        _defaultPreferencesPath = Path.GetFullPath(defaultPreferencesPath);
        _preferencesPath = StorePathBootstrap.ResolveActivePath(_defaultPreferencesPath);
        _catalog = catalog;
        _logger = logger;
    }

    public string PreferencesPath => Volatile.Read(ref _preferencesPath);

    public string DefaultPreferencesPath => _defaultPreferencesPath;

    public Task<StoreRelocationResult> RelocateAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var current = Volatile.Read(ref _preferencesPath);
            var result = await StorePathRelocator.RelocateAsync(current, _defaultPreferencesPath, targetDirectory, ct).ConfigureAwait(false);
            if (result.Succeeded && !string.Equals(result.ActivePath, current, StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _preferencesPath, result.ActivePath);
                _logger.LogInformation("Relocated user preferences store from {OldPath} to {NewPath}.", current, result.ActivePath);
            }
            else if (!result.Succeeded)
            {
                _logger.LogWarning("User preferences store relocation to '{TargetDirectory}' failed: {Message}", targetDirectory, result.Message);
            }

            return result;
        }, cancellationToken);
    }

    public Task<UserUnitPreferencesSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var preferencesPath = Volatile.Read(ref _preferencesPath);
            if (!File.Exists(preferencesPath))
            {
                return (UserUnitPreferencesSnapshot?)null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(preferencesPath, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return (UserUnitPreferencesSnapshot?)null;
                }

                var snapshot = JsonSerializer.Deserialize<UserUnitPreferencesSnapshot>(json, SerializerOptions);
                return (UserUnitPreferencesSnapshot?)_catalog.Normalize(snapshot);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "User preferences file {PreferencesPath} contained invalid JSON.", preferencesPath);
                return (UserUnitPreferencesSnapshot?)null;
            }
        }, cancellationToken);
    }

    public Task WriteAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);

        return _writeQueue.EnqueueAsync(async ct =>
        {
            var normalized = _catalog.Normalize(snapshot);
            var preferencesPath = Volatile.Read(ref _preferencesPath);
            var directoryPath = Path.GetDirectoryName(preferencesPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var temporaryPath = $"{preferencesPath}.tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(normalized, SerializerOptions), ct).ConfigureAwait(false);
            File.Move(temporaryPath, preferencesPath, overwrite: true);

            _logger.LogInformation("Persisted user preferences to {PreferencesPath}.", preferencesPath);
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writeQueue.Dispose();
        _disposed = true;
    }
}
