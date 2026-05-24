using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Extensions.Options;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkServiceConfigurationManager : IDisposable
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly FrameworkServiceConfigurationStore _store;
    private readonly ReactiveRequestQueue _queue = new();
    private readonly BehaviorSubject<FrameworkServiceConfigurationSnapshot> _snapshotSubject;
    private readonly ILogger<FrameworkServiceConfigurationManager> _logger;
    private FrameworkServiceOptions _currentOptions;
    private bool _disposed;

    public FrameworkServiceConfigurationManager(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlAuthorizationService authorizationService,
        IOptionsMonitor<FrameworkServiceOptions> optionsMonitor,
        FrameworkServiceConfigurationStore store,
        ILogger<FrameworkServiceConfigurationManager> logger)
    {
        ArgumentNullException.ThrowIfNull(frameworkDataProvider);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _store = store;
        _logger = logger;
        _currentOptions = optionsMonitor.CurrentValue;
        _snapshotSubject = new BehaviorSubject<FrameworkServiceConfigurationSnapshot>(CreateSnapshot(_currentOptions));
    }

    public FrameworkServiceConfigurationSnapshot GetCurrentSnapshot() => _snapshotSubject.Value;

    public IObservable<FrameworkServiceConfigurationSnapshot> WatchSnapshot() => _snapshotSubject.AsObservable();

    public FrameworkServiceConfigurationSnapshot CreateSnapshot(FrameworkServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new FrameworkServiceConfigurationSnapshot
        {
            PollingInterval = options.PollingInterval,
            HardwareInfoPollingInterval = options.HardwareInfoPollingInterval,
            AllowFanControlCommands = options.AllowFanControlCommands,
            PersistentConfigurationPath = _store.PersistentConfigurationPath,
        };
    }

    public Task<FrameworkServiceConfigurationOperationResult> ApplyAsync(FrameworkServiceConfigurationApplyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TryValidate(request, out var validationError))
        {
            return Task.FromResult(new FrameworkServiceConfigurationOperationResult
            {
                Succeeded = false,
                Message = validationError,
                Configuration = GetCurrentSnapshot(),
            });
        }

        return _queue.EnqueueAsync(async ct =>
        {
            var previousOptions = _currentOptions;
            var updatedOptions = previousOptions with
            {
                PollingInterval = request.PollingInterval,
                HardwareInfoPollingInterval = request.HardwareInfoPollingInterval,
                AllowFanControlCommands = request.AllowFanControlCommands,
            };

            if (updatedOptions == previousOptions)
            {
                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = true,
                    Message = "Service configuration already matches the requested values.",
                    Configuration = GetCurrentSnapshot(),
                };
            }

            var shouldRunFrameworkPolling = _frameworkDataProvider.IsPolling;
            var shouldRunHardwareInfoPolling = _frameworkDataProvider.IsHardwareInfoPolling;

            try
            {
                _logger.LogInformation(
                    "Applying service configuration. PollingInterval={PollingInterval}, HardwareInfoPollingInterval={HardwareInfoPollingInterval}, AllowFanControlCommands={AllowFanControlCommands}.",
                    updatedOptions.PollingInterval,
                    updatedOptions.HardwareInfoPollingInterval,
                    updatedOptions.AllowFanControlCommands);

                await ApplyRuntimeConfigurationAsync(updatedOptions, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling, ct).ConfigureAwait(false);
                _currentOptions = updatedOptions;
                var snapshot = CreateSnapshot(updatedOptions);
                _snapshotSubject.OnNext(snapshot);

                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = true,
                    Message = "Applied the updated service configuration. Use Save to persist it.",
                    Configuration = snapshot,
                };
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to apply service configuration. Attempting to restore the previous runtime configuration.");

                var rollbackSucceeded = await TryRollbackRuntimeAsync(previousOptions, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling).ConfigureAwait(false);
                var resultMessage = rollbackSucceeded
                    ? $"Failed to apply the service configuration. The previous runtime configuration was restored. {exception.Message}"
                    : $"Failed to apply the service configuration and the rollback attempt also failed. {exception.Message}";

                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = false,
                    Message = resultMessage,
                    Configuration = rollbackSucceeded ? CreateSnapshot(previousOptions) : CreateSnapshot(updatedOptions),
                };
            }
        }, cancellationToken);
    }

    public Task<FrameworkServiceConfigurationOperationResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        return _queue.EnqueueAsync(async ct =>
        {
            var options = _currentOptions;
            try
            {
                await _store.WriteAsync(options, ct).ConfigureAwait(false);
                _logger.LogInformation("Saved current service configuration to {PersistentConfigurationPath}.", _store.PersistentConfigurationPath);

                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = true,
                    Message = $"Saved the current service configuration to {_store.PersistentConfigurationPath}.",
                    Configuration = CreateSnapshot(options),
                };
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to save service configuration.");
                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = false,
                    Message = $"Failed to save the service configuration. {exception.Message}",
                    Configuration = CreateSnapshot(options),
                };
            }
        }, cancellationToken);
    }

    public Task<FrameworkServiceConfigurationOperationResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        return _queue.EnqueueAsync(async ct =>
        {
            FrameworkServiceOptions? loaded;
            try
            {
                loaded = await _store.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to read service configuration from {PersistentConfigurationPath}.", _store.PersistentConfigurationPath);
                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = false,
                    Message = $"Failed to read the service configuration. {exception.Message}",
                    Configuration = CreateSnapshot(_currentOptions),
                };
            }

            if (loaded is null)
            {
                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = false,
                    Message = $"No persisted service configuration was found at {_store.PersistentConfigurationPath}.",
                    Configuration = CreateSnapshot(_currentOptions),
                };
            }

            var request = new FrameworkServiceConfigurationApplyRequest
            {
                PollingInterval = loaded.PollingInterval,
                HardwareInfoPollingInterval = loaded.HardwareInfoPollingInterval,
                AllowFanControlCommands = loaded.AllowFanControlCommands,
            };

            if (TryValidate(request, out var validationError))
            {
                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = false,
                    Message = $"Persisted service configuration is invalid: {validationError}",
                    Configuration = CreateSnapshot(_currentOptions),
                };
            }

            var previousOptions = _currentOptions;
            var shouldRunFrameworkPolling = _frameworkDataProvider.IsPolling;
            var shouldRunHardwareInfoPolling = _frameworkDataProvider.IsHardwareInfoPolling;

            try
            {
                await ApplyRuntimeConfigurationAsync(loaded, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling, ct).ConfigureAwait(false);
                _currentOptions = loaded;
                var snapshot = CreateSnapshot(loaded);
                _snapshotSubject.OnNext(snapshot);
                _logger.LogInformation("Loaded service configuration from {PersistentConfigurationPath}.", _store.PersistentConfigurationPath);

                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = true,
                    Message = $"Loaded and applied the service configuration from {_store.PersistentConfigurationPath}.",
                    Configuration = snapshot,
                };
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to apply loaded service configuration.");
                var rollbackSucceeded = await TryRollbackRuntimeAsync(previousOptions, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling).ConfigureAwait(false);
                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = false,
                    Message = rollbackSucceeded
                        ? $"Failed to apply the loaded service configuration. The previous runtime configuration was restored. {exception.Message}"
                        : $"Failed to apply the loaded service configuration and the rollback attempt also failed. {exception.Message}",
                    Configuration = CreateSnapshot(rollbackSucceeded ? previousOptions : loaded),
                };
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _snapshotSubject.OnCompleted();
        _snapshotSubject.Dispose();
        _queue.Dispose();
    }

    public Task<FrameworkServiceConfigurationOperationResult> RelocateAsync(string targetDirectory, CancellationToken cancellationToken = default)
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
                _logger.LogError(exception, "Failed to relocate service configuration store to '{TargetDirectory}'.", targetDirectory);
                return new FrameworkServiceConfigurationOperationResult
                {
                    Succeeded = false,
                    Message = $"Failed to relocate the service configuration store. {exception.Message}",
                    Configuration = CreateSnapshot(_currentOptions),
                };
            }

            if (relocation.Succeeded)
            {
                _snapshotSubject.OnNext(CreateSnapshot(_currentOptions));
            }

            return new FrameworkServiceConfigurationOperationResult
            {
                Succeeded = relocation.Succeeded,
                Message = relocation.Message,
                Configuration = CreateSnapshot(_currentOptions),
            };
        }, cancellationToken);
    }

    private async Task ApplyRuntimeConfigurationAsync(
        FrameworkServiceOptions options,
        bool shouldRunFrameworkPolling,
        bool shouldRunHardwareInfoPolling,
        CancellationToken cancellationToken)
    {
        if (_frameworkDataProvider.IsPolling && !_frameworkDataProvider.StopPolling())
        {
            throw new InvalidOperationException("Unable to stop the Framework polling loop before applying the updated configuration.");
        }

        if (_frameworkDataProvider.IsHardwareInfoPolling && !_frameworkDataProvider.StopHardwareInfoPolling())
        {
            throw new InvalidOperationException("Unable to stop the HardwareInfo polling loop before applying the updated configuration.");
        }

        if (!_frameworkDataProvider.SetPolling(options.PollingInterval))
        {
            throw new InvalidOperationException("Unable to configure the Framework polling interval while applying the updated service configuration.");
        }

        if (!_frameworkDataProvider.SetHardwareInfoPolling(options.HardwareInfoPollingInterval))
        {
            throw new InvalidOperationException("Unable to configure the HardwareInfo polling interval while applying the updated service configuration.");
        }

        if (shouldRunFrameworkPolling && !_frameworkDataProvider.StartPolling())
        {
            throw new InvalidOperationException("Unable to restart the Framework polling loop after applying the updated service configuration.");
        }

        if (shouldRunHardwareInfoPolling && !_frameworkDataProvider.StartHardwareInfoPolling())
        {
            throw new InvalidOperationException("Unable to restart the HardwareInfo polling loop after applying the updated service configuration.");
        }

        _frameworkDataProvider.SetFanControlAuthorization(
            options.AllowFanControlCommands,
            _authorizationService.HasCallerIdentityValidation,
            _authorizationService.GetAuthorizationMessage(options.AllowFanControlCommands));

        await _frameworkDataProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryRollbackRuntimeAsync(
        FrameworkServiceOptions previousOptions,
        bool shouldRunFrameworkPolling,
        bool shouldRunHardwareInfoPolling)
    {
        try
        {
            await ApplyRuntimeConfigurationAsync(previousOptions, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("Rolled back the runtime service configuration to the previous values.");
            return true;
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "Failed to roll back the runtime service configuration to the previous values.");
            return false;
        }
    }

    private static bool TryValidate(FrameworkServiceConfigurationApplyRequest request, out string validationError)
    {
        if (request.PollingInterval <= TimeSpan.Zero)
        {
            validationError = "Telemetry polling interval must be greater than zero milliseconds.";
            return true;
        }

        if (request.HardwareInfoPollingInterval <= TimeSpan.Zero)
        {
            validationError = "Hardware info polling interval must be greater than zero milliseconds.";
            return true;
        }

        validationError = string.Empty;
        return false;
    }
}
