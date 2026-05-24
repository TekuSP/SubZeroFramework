using System.Reactive.Linq;
using System.Reactive.Subjects;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkUserPreferencesManager : IDisposable
{
    private readonly FrameworkUserPreferencesStore _store;
    private readonly UnitPreferenceCatalog _catalog;
    private readonly ReactiveRequestQueue _queue = new();
    private readonly BehaviorSubject<UserPreferencesState> _stateSubject;
    private readonly ILogger<FrameworkUserPreferencesManager> _logger;
    private UserUnitPreferencesSnapshot _current;
    private bool _disposed;

    public FrameworkUserPreferencesManager(
        FrameworkUserPreferencesStore store,
        UnitPreferenceCatalog catalog,
        ILogger<FrameworkUserPreferencesManager> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _catalog = catalog;
        _logger = logger;
        _current = catalog.CreateDefaultSnapshot();
        _stateSubject = new BehaviorSubject<UserPreferencesState>(CreateState(_current));

        TryLoadOnStartup();
    }

    public UserPreferencesState GetCurrentState() => _stateSubject.Value;

    public IObservable<UserPreferencesState> WatchState() => _stateSubject.AsObservable();

    public string PreferencesPath => _store.PreferencesPath;

    public Task<UserPreferencesOperationResult> ApplyAsync(UserUnitPreferencesSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return _queue.EnqueueAsync(ct =>
        {
            var normalized = _catalog.Normalize(snapshot);
            _current = normalized;
            var state = CreateState(normalized);
            _stateSubject.OnNext(state);
            _logger.LogInformation("Applied user preferences update.");

            return Task.FromResult(new UserPreferencesOperationResult
            {
                Succeeded = true,
                Message = "Applied the updated preferences. Use Save to persist them.",
                Preferences = normalized,
                PreferencesPath = _store.PreferencesPath,
            });
        }, cancellationToken);
    }

    public Task<UserPreferencesOperationResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        return _queue.EnqueueAsync(async ct =>
        {
            var snapshot = _current;
            try
            {
                await _store.WriteAsync(snapshot, ct).ConfigureAwait(false);
                return new UserPreferencesOperationResult
                {
                    Succeeded = true,
                    Message = $"Saved the current preferences to {_store.PreferencesPath}.",
                    Preferences = snapshot,
                    PreferencesPath = _store.PreferencesPath,
                };
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to save user preferences.");
                return new UserPreferencesOperationResult
                {
                    Succeeded = false,
                    Message = $"Failed to save the preferences. {exception.Message}",
                    Preferences = snapshot,
                    PreferencesPath = _store.PreferencesPath,
                };
            }
        }, cancellationToken);
    }

    public Task<UserPreferencesOperationResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        return _queue.EnqueueAsync(async ct =>
        {
            UserUnitPreferencesSnapshot? loaded;
            try
            {
                loaded = await _store.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to read user preferences.");
                return new UserPreferencesOperationResult
                {
                    Succeeded = false,
                    Message = $"Failed to read the preferences. {exception.Message}",
                    Preferences = _current,
                    PreferencesPath = _store.PreferencesPath,
                };
            }

            if (loaded is null)
            {
                return new UserPreferencesOperationResult
                {
                    Succeeded = false,
                    Message = $"No persisted preferences were found at {_store.PreferencesPath}.",
                    Preferences = _current,
                    PreferencesPath = _store.PreferencesPath,
                };
            }

            _current = loaded;
            var state = CreateState(loaded);
            _stateSubject.OnNext(state);

            return new UserPreferencesOperationResult
            {
                Succeeded = true,
                Message = $"Loaded preferences from {_store.PreferencesPath}.",
                Preferences = loaded,
                PreferencesPath = _store.PreferencesPath,
            };
        }, cancellationToken);
    }

    public Task<UserPreferencesOperationResult> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        return _queue.EnqueueAsync(ct =>
        {
            var defaults = _catalog.CreateDefaultSnapshot();
            _current = defaults;
            var state = CreateState(defaults);
            _stateSubject.OnNext(state);

            return Task.FromResult(new UserPreferencesOperationResult
            {
                Succeeded = true,
                Message = "Restored default preferences. Use Save to persist them.",
                Preferences = defaults,
                PreferencesPath = _store.PreferencesPath,
            });
        }, cancellationToken);
    }

    public Task<UserPreferencesOperationResult> RelocateAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        return _queue.EnqueueAsync(async ct =>
        {
            StoreRelocationResult relocation;
            try
            {
                relocation = await _store.RelocateAsync(targetDirectory, ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to relocate user preferences store to '{TargetDirectory}'.", targetDirectory);
                return new UserPreferencesOperationResult
                {
                    Succeeded = false,
                    Message = $"Failed to relocate the preferences store. {exception.Message}",
                    Preferences = _current,
                    PreferencesPath = _store.PreferencesPath,
                };
            }

            if (relocation.Succeeded)
            {
                _stateSubject.OnNext(CreateState(_current));
            }

            return new UserPreferencesOperationResult
            {
                Succeeded = relocation.Succeeded,
                Message = relocation.Message,
                Preferences = _current,
                PreferencesPath = _store.PreferencesPath,
            };
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
        _queue.Dispose();
    }

    private void TryLoadOnStartup()
    {
        try
        {
            var loaded = _store.ReadAsync().GetAwaiter().GetResult();
            if (loaded is not null)
            {
                _current = loaded;
                _stateSubject.OnNext(CreateState(loaded));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load user preferences on startup.");
        }
    }

    private UserPreferencesState CreateState(UserUnitPreferencesSnapshot snapshot)
    {
        return new UserPreferencesState
        {
            Preferences = snapshot,
            PreferencesPath = _store.PreferencesPath,
        };
    }
}
