using DesktopNotifications;

namespace SubZeroFramework.Services;

/// <summary>
/// Owns the platform desktop-notification manager and delivers all of the app's notifications.
/// </summary>
public interface IDesktopNotificationService
{
    /// <summary>Initializes the platform notification manager (idempotent; safe to call once at app start).</summary>
    void Start();

    /// <summary>Shows a notification unconditionally (callers gate on their own opt-in, e.g. thermal alerts).</summary>
    Task<bool> TryShowAsync(string title, string body);

    /// <summary>
    /// Shows a status notification, honoring the "Status notifications" opt-in from
    /// Settings → Startup &amp; alerts. Returns false when the opt-in is off or delivery failed.
    /// </summary>
    Task<bool> TryShowStatusAsync(string title, string body);
}

/// <summary>
/// Delivery goes through the DesktopNotificationsFixed library: on Windows the classic shortcut +
/// AppUserModelId registration with legacy <c>Windows.UI.Notifications</c> toasts (WinAppSDK's
/// <c>AppNotificationManager.Show()</c> was silently dropped for this self-contained unpackaged app —
/// see ThermalAlertMonitor's git history for the 2026-07-18/19 investigation), and on Linux the
/// <c>org.freedesktop.Notifications</c> D-Bus interface every desktop environment implements.
/// </summary>
public sealed class DesktopNotificationService : IDesktopNotificationService, IDisposable
{
    private readonly ILocalClientSettingsStore _clientSettings;
    private readonly ILogger<DesktopNotificationService> _logger;
    private INotificationManager? _notificationManager;
    private bool _started;

    public DesktopNotificationService(ILocalClientSettingsStore clientSettings, ILogger<DesktopNotificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(clientSettings);
        ArgumentNullException.ThrowIfNull(logger);

        _clientSettings = clientSettings;
        _logger = logger;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ = InitializeNotificationManagerAsync();
    }

    public async Task<bool> TryShowAsync(string title, string body)
    {
        if (_notificationManager is not { } manager)
        {
            return false;
        }

        try
        {
            await manager.ShowNotification(new Notification { Title = title, Body = body }).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to show the {Title} notification.", title);
            return false;
        }
    }

    public Task<bool> TryShowStatusAsync(string title, string body)
        => _clientSettings.StatusNotificationsEnabled ? TryShowAsync(title, body) : Task.FromResult(false);

    public void Dispose()
    {
        (_notificationManager as IDisposable)?.Dispose();
    }

    // Creates + initializes the platform notification manager. On Windows, Initialize registers the
    // AppUserModelId (shortcut-based, the mechanism that reliably displays toasts for an unpackaged app);
    // on Linux it connects to the org.freedesktop.Notifications session-bus service. The manager is only
    // published to _notificationManager after Initialize succeeds, so Show never races registration.
    private async Task InitializeNotificationManagerAsync()
    {
        try
        {
#if WINDOWS10_0_26100_0_OR_GREATER
            var manager = new DesktopNotifications.Windows.WindowsNotificationManager(
                DesktopNotifications.Windows.WindowsApplicationContext.FromCurrentProcess(
                    "SubZero Framework Edition",
                    "tekusp.SubZeroFramework"));
#else
            if (!OperatingSystem.IsLinux())
            {
                // The desktop (Skia) target ships as the Linux build, where the FreeDesktop manager below
                // handles delivery. Running this target on a Windows host (a local-dev convenience only —
                // Windows releases use the WinUI target) has no D-Bus session bus, so notifications stay
                // log-only.
                return;
            }

            var manager = new DesktopNotifications.FreeDesktop.FreeDesktopNotificationManager();
#endif
            await manager.Initialize().ConfigureAwait(false);
            _notificationManager = manager;
            _logger.LogInformation("Desktop notification manager initialized.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Desktop notification initialization failed; notifications will only reach the log.");
        }
    }
}
