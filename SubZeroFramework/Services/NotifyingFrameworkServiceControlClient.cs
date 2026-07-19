namespace SubZeroFramework.Services;

/// <summary>
/// Decorates the platform service-control client so every lifecycle action the user takes — from the
/// Settings page, the Warnings recovery page, or anywhere else — raises a status notification with the
/// action's outcome. Delivery honors the "Status notifications" opt-in (gated inside
/// <see cref="IDesktopNotificationService.TryShowStatusAsync"/>), so this decorator stays unconditional.
/// </summary>
public sealed class NotifyingFrameworkServiceControlClient : IFrameworkServiceControlClient
{
    private readonly IFrameworkServiceControlClient _inner;
    private readonly IDesktopNotificationService _notificationService;

    public NotifyingFrameworkServiceControlClient(IFrameworkServiceControlClient inner, IDesktopNotificationService notificationService)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(notificationService);

        _inner = inner;
        _notificationService = notificationService;
    }

    public FrameworkServiceControlInfo GetInfo() => _inner.GetInfo();

    public Task<FrameworkServiceCommandResult> ShutdownAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.ShutdownAsync(cancellationToken), "Service stopped", "Stopping the service failed");

    public Task<FrameworkServiceCommandResult> RestartAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.RestartAsync(cancellationToken), "Service restarted", "Restarting the service failed");

    public Task<FrameworkServiceCommandResult> EnableAutorunAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.EnableAutorunAsync(cancellationToken), "Service auto-start enabled", "Enabling service auto-start failed");

    public Task<FrameworkServiceCommandResult> DisableAutorunAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.DisableAutorunAsync(cancellationToken), "Service auto-start disabled", "Disabling service auto-start failed");

    public Task<FrameworkServiceCommandResult> InstallAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.InstallAsync(cancellationToken), "Service installed", "Installing the service failed");

    public Task<FrameworkServiceCommandResult> UpdateAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.UpdateAsync(cancellationToken), "Service updated", "Updating the service failed");

    public Task<FrameworkServiceCommandResult> UninstallAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.UninstallAsync(cancellationToken), "Service uninstalled", "Uninstalling the service failed");

    public Task<FrameworkServiceCommandResult> ReinstallAsync(CancellationToken cancellationToken = default)
        => NotifyAsync(_inner.ReinstallAsync(cancellationToken), "Service reinstalled", "Reinstalling the service failed");

    private async Task<FrameworkServiceCommandResult> NotifyAsync(
        Task<FrameworkServiceCommandResult> operation,
        string successTitle,
        string failureTitle)
    {
        var result = await operation.ConfigureAwait(false);

        // Fire-and-forget: the notification must never delay or fail the actual operation result.
        _ = _notificationService.TryShowStatusAsync(result.Succeeded ? successTitle : failureTitle, result.Message);

        return result;
    }
}
