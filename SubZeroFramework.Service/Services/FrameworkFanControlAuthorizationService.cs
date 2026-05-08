using Microsoft.Extensions.Options;

using SubZeroFramework.Service.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Evaluates whether mutating fan-control RPCs may be served by the local background service.
/// </summary>
public sealed class FrameworkFanControlAuthorizationService
{
    private readonly IOptionsMonitor<FrameworkServiceOptions> _optionsMonitor;

    public FrameworkFanControlAuthorizationService(IOptionsMonitor<FrameworkServiceOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _optionsMonitor = optionsMonitor;
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
    {
        if (!IsFanControlEnabled)
        {
            return "Fan-control RPCs are disabled by service configuration until local caller identity validation is available for this IPC transport.";
        }

        if (!HasCallerIdentityValidation)
        {
            return "Fan-control RPCs are enabled by configuration, but this transport does not currently expose portable caller identity validation on the server.";
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
            throw new InvalidOperationException(GetAuthorizationMessage());
        }
    }
}
