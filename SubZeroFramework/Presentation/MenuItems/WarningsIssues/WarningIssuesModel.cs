using CommunityToolkit.Mvvm.ComponentModel;

using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation.MenuItems.WarningsIssues;

public partial class WarningIssuesModel : ObservableObject, IDisposable
{
    private readonly IFrameworkStatusClient _frameworkStatusClient;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly CompositeDisposable _subscriptions = [];

    public WarningIssuesModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IFrameworkStatusClient frameworkStatusClient,
        SynchronizationContext synchronizationContext)
    {
        _frameworkStatusClient = frameworkStatusClient;
        _synchronizationContext = synchronizationContext;
        StatusTitle = "Waiting for service status";
        StatusMessage = "Checking SubZeroFramework.Service health.";
        _frameworkStatusClient
            .WatchStatus()
            .ObserveOn(_synchronizationContext)
            .Subscribe(UpdateStatus)
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    public partial string StatusTitle { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    private void UpdateStatus(FrameworkSystemStatus status)
    {
        if (!status.IsGrpcActive)
        {
            StatusTitle = "Background service offline";
            StatusMessage = "The UI cannot reach SubZeroFramework.Service over local gRPC IPC.";
            return;
        }

        if (!status.IsLibraryAvailable)
        {
            StatusTitle = "FrameworkDotnet not found";
            StatusMessage = "The background service could not load the required Framework library.";
            return;
        }

        if (status.RequiresElevation)
        {
            StatusTitle = "Service elevation required";
            StatusMessage = "The Linux service must run as root for Framework EC access.";
            return;
        }

        if (status.IsFrameworkDevice != true)
        {
            StatusTitle = "Framework device not detected";
            StatusMessage = "The background service is running, but the current machine is not reporting supported Framework hardware.";
            return;
        }

        if (!string.IsNullOrEmpty(status.LastError))
        {
            StatusTitle = "Background service warning";
            StatusMessage = status.LastError;
            return;
        }

        if (!status.IsFanControlEnabled)
        {
            StatusTitle = "Fan control disabled";
            StatusMessage = status.FanControlAuthorizationMessage ?? "Fan-control RPCs are currently disabled by the background service.";
            return;
        }

        if (!status.HasCallerIdentityValidation)
        {
            StatusTitle = "Fan control validation limited";
            StatusMessage = status.FanControlAuthorizationMessage ?? "The service cannot currently validate caller identity for fan-control RPCs on this IPC transport.";
            return;
        }

        StatusTitle = "Background service healthy";
        StatusMessage = "Status streaming is connected and the service reports healthy Framework telemetry access.";
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }
}
