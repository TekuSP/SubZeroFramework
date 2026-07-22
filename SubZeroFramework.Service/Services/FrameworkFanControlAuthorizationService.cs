using Microsoft.Extensions.Options;

using SubZeroFramework.Service.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Evaluates whether mutating fan-control RPCs may be served by the local background service.
/// </summary>
public sealed class FrameworkFanControlAuthorizationService
{
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;
    private readonly ILogger<FrameworkFanControlAuthorizationService> _logger;

    public FrameworkFanControlAuthorizationService(IOptionsMonitor<FrameworkServiceOptions> optionsMonitor, ILogger<FrameworkFanControlAuthorizationService> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Gets whether the current platform can validate local caller identity for the configured IPC transport.
    /// </summary>
    public bool HasCallerIdentityValidation => false;

    /// <summary>
    /// Gets whether fan-control commands are enabled by service configuration.
    /// </summary>
    public bool IsFanControlEnabled => _optionsMonitor.CurrentValue.AllowFanControlCommands;

    /// <summary>
    /// Gets the current authorization message for mutating fan-control commands.
    /// </summary>
    public string GetAuthorizationMessage()
        => GetAuthorizationMessage(IsFanControlEnabled);

    /// <summary>
    /// Gets the authorization message for a supplied fan-control enabled value.
    /// </summary>
    /// <remarks>
    /// The disabled message must tell the user HOW to enable fan control, and nothing else. An earlier
    /// version said commands were disabled "until local caller identity validation is available for this
    /// IPC transport" — stale wording from before the decision to ship with caller-identity validation
    /// off, and it read as an unfixable IPC error: the first Linux tester concluded fan control could not
    /// be enabled at all, when it was one documented toggle away. Do not reintroduce technical
    /// justifications here; the security posture lives in SECURITY.md and
    /// Docs/IpcAuthorizationAndUiCadence.md.
    /// </remarks>
    public string GetAuthorizationMessage(bool isFanControlEnabled)
    {
        if (!isFanControlEnabled)
        {
            return "Fan-control commands are switched off. Turn on \"Allow fan control commands\" under Settings → Service, then apply.";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Fan-control RPCs are enabled for local Linux clients on the validated Unix socket endpoint.";
        }

        if (OperatingSystem.IsWindows())
        {
            return "Fan-control RPCs are enabled for local Windows clients on the validated Unix socket endpoint.";
        }

        return "Fan-control RPCs are enabled for the validated local IPC endpoint.";
    }

    /// <summary>
    /// Throws when mutating fan-control commands are not authorized.
    /// </summary>
    public void EnsureCommandAccess()
    {
        if (!IsFanControlEnabled)
        {
            var message = GetAuthorizationMessage();
            _logger.LogWarning("Rejected fan-control command because authorization failed. Message={AuthorizationMessage}.", message);
            throw new InvalidOperationException(message);
        }
    }
}
