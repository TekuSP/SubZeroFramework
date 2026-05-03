namespace SubZeroFramework.Presentation;

public class ShellModel
{
    private readonly INavigator _navigator;

    public ShellModel(
        INavigator navigator)
    {
        _navigator = navigator;

        Thread.Sleep(2500);
        _ = _navigator.NavigateRouteAsync(this, "-/Main");
    }
}
