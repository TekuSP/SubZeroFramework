using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using SubZeroFramework.Models;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service.Services;

public sealed class FrameworkServiceConfigurationManager
{
    private readonly IFrameworkDataProvider _frameworkDataProvider;
    private readonly FrameworkFanControlAuthorizationService _authorizationService;
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;
    private readonly IConfigurationRoot _configurationRoot;
    private readonly FrameworkServiceConfigurationStore _store;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly ILogger<FrameworkServiceConfigurationManager> _logger;

    public FrameworkServiceConfigurationManager(
        IFrameworkDataProvider frameworkDataProvider,
        FrameworkFanControlAuthorizationService authorizationService,
        IOptionsMonitor<FrameworkServiceOptions> optionsMonitor,
        IConfiguration configuration,
        FrameworkServiceConfigurationStore store,
        ILogger<FrameworkServiceConfigurationManager> logger)
    {
        ArgumentNullException.ThrowIfNull(frameworkDataProvider);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _frameworkDataProvider = frameworkDataProvider;
        _authorizationService = authorizationService;
        _optionsMonitor = optionsMonitor;
        _configurationRoot = configuration as IConfigurationRoot
            ?? throw new InvalidOperationException("The service configuration root does not support reload semantics.");
        _store = store;
        _logger = logger;
    }

    public FrameworkServiceConfigurationSnapshot GetCurrentSnapshot()
        => CreateSnapshot(_optionsMonitor.CurrentValue);

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

    public async Task<FrameworkServiceConfigurationUpdateResult> UpdateAsync(FrameworkServiceConfigurationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TryValidate(request, out var validationError))
        {
            var currentSnapshot = GetCurrentSnapshot();
            return new FrameworkServiceConfigurationUpdateResult
            {
                Succeeded = false,
                Message = validationError,
                Configuration = currentSnapshot,
            };
        }

        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var previousOptions = _optionsMonitor.CurrentValue;
            var updatedOptions = previousOptions with
            {
                PollingInterval = request.PollingInterval,
                HardwareInfoPollingInterval = request.HardwareInfoPollingInterval,
                AllowFanControlCommands = request.AllowFanControlCommands,
            };

            if (updatedOptions == previousOptions)
            {
                return new FrameworkServiceConfigurationUpdateResult
                {
                    Succeeded = true,
                    Message = "Service configuration already matches the requested values.",
                    Configuration = CreateSnapshot(updatedOptions),
                };
            }

            var shouldRunFrameworkPolling = _frameworkDataProvider.IsPolling;
            var shouldRunHardwareInfoPolling = _frameworkDataProvider.IsHardwareInfoPolling;

            try
            {
                _logger.LogInformation(
                    "Updating service configuration. PollingInterval={PollingInterval}, HardwareInfoPollingInterval={HardwareInfoPollingInterval}, AllowFanControlCommands={AllowFanControlCommands}.",
                    updatedOptions.PollingInterval,
                    updatedOptions.HardwareInfoPollingInterval,
                    updatedOptions.AllowFanControlCommands);

                await _store.WriteAsync(updatedOptions, cancellationToken).ConfigureAwait(false);
                ReloadConfiguration();
                await ApplyRuntimeConfigurationAsync(updatedOptions, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling, cancellationToken).ConfigureAwait(false);

                return new FrameworkServiceConfigurationUpdateResult
                {
                    Succeeded = true,
                    Message = "Applied and persisted the updated service configuration.",
                    Configuration = CreateSnapshot(updatedOptions),
                };
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to update service configuration. Attempting rollback to the previous configuration.");

                var rollbackSucceeded = await TryRollbackAsync(previousOptions, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling).ConfigureAwait(false);
                var resultMessage = rollbackSucceeded
                    ? $"Failed to update the service configuration. The previous configuration was restored. {exception.Message}"
                    : $"Failed to update the service configuration and the rollback attempt also failed. {exception.Message}";

                return new FrameworkServiceConfigurationUpdateResult
                {
                    Succeeded = false,
                    Message = resultMessage,
                    Configuration = rollbackSucceeded ? CreateSnapshot(previousOptions) : CreateSnapshot(updatedOptions),
                };
            }
        }
        finally
        {
            _updateGate.Release();
        }
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

    private void ReloadConfiguration()
    {
        _configurationRoot.Reload();
    }

    private async Task<bool> TryRollbackAsync(
        FrameworkServiceOptions previousOptions,
        bool shouldRunFrameworkPolling,
        bool shouldRunHardwareInfoPolling)
    {
        try
        {
            await _store.WriteAsync(previousOptions, CancellationToken.None).ConfigureAwait(false);
            ReloadConfiguration();
            await ApplyRuntimeConfigurationAsync(previousOptions, shouldRunFrameworkPolling, shouldRunHardwareInfoPolling, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("Rolled back the service configuration to the previous values.");
            return true;
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "Failed to roll back the service configuration to the previous values.");
            return false;
        }
    }

    private static bool TryValidate(FrameworkServiceConfigurationUpdateRequest request, out string validationError)
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
