using CommunityToolkit.WinUI;

using Hardware.Info;

using Microsoft.UI.Dispatching;

using SubZeroFramework.Services;

namespace SubZeroFramework.Presentation;

public class ShellModel
{
    private readonly INavigator _navigator;

    public ShellModel(
        INavigator navigator, DispatcherQueue dispatcherQueue, IFrameworkDataProvider dataProvider, IHardwareInfo hardwareInfo)
    {
        _navigator = navigator;

        Task.Run(async () =>
        {
            await dispatcherQueue.EnqueueAsync(() =>
            {
                hardwareInfo.RefreshCPUList(false, 500, false);
                hardwareInfo.RefreshMemoryList();
            });

            var lastStatus = await dataProvider.RefreshAsync();

            if (lastStatus.IsLibraryAvailable && lastStatus.IsFrameworkDevice == true) //Proactively start polling if the library is available and it's a framework device, otherwise wait for user to navigate to main page where polling will be started
            {
                dataProvider.SetPolling(TimeSpan.FromSeconds(1)); //This should be read from config file
                dataProvider.StartPolling();
            }

            await _navigator.NavigateRouteAsync(this, "-/Main");
        });
    }
}
