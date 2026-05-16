using CommunityToolkit.WinUI;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation;

public class ShellModel
{
    private readonly INavigator _navigator;

    public ShellModel(
        INavigator navigator, DispatcherQueue dispatcherQueue, IFrameworkStatusClient frameworkStatusClient)
    {
        _navigator = navigator;

        Task.Run(async () =>
        {
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
