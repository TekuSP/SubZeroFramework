using Uno.UI.Hosting;

namespace SubZeroFramework;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
#if DEBUG
            .UseWin32() //No more Win32 support, run WinUI3, NO PRODUCTION RELEASE
#endif
            .Build();

        host.Run();
    }
}
