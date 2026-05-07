using CommunityToolkit.WinUI;

using Hardware.Info;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation;

public class ShellModel
{
    private readonly INavigator _navigator;

    public ShellModel(
        INavigator navigator, DispatcherQueue dispatcherQueue, IFrameworkStatusClient frameworkStatusClient, IHardwareInfo hardwareInfo)
    {
        _navigator = navigator;

        Task.Run(async () =>
        {
            await dispatcherQueue.EnqueueAsync(() =>
            {
                hardwareInfo.RefreshCPUList(false, 500, false);
                hardwareInfo.RefreshMemoryList();
            });

            var lastStatus = await frameworkStatusClient.GetStatusAsync().ConfigureAwait(false);

            if (lastStatus.IsGrpcActive && lastStatus.IsLibraryAvailable && lastStatus.IsFrameworkDevice == true)
            {
                await dispatcherQueue.EnqueueAsync(async () =>
                {
                    await _navigator.NavigateRouteAsync(this, "/Main/Dashboard");
                });
            }
            else
            {
                await dispatcherQueue.EnqueueAsync(async () =>
                {
                    await _navigator.NavigateRouteAsync(this, "/Main/WarningIssues"); //We have a problem, navigate to warning page to show the user
                });
            }

        });
    }
}
