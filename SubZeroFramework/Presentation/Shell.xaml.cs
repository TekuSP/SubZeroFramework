using Microsoft.UI.Xaml.Media.Imaging;

namespace SubZeroFramework.Presentation;

public sealed partial class Shell : UserControl, IContentControlProvider
{
    public Shell()
    {
        this.InitializeComponent();

#if WINDOWS
        SplashLogo.Source = new BitmapImage(new Uri("ms-appx:///Assets/Svg/main_logo.png"));
#else
        SplashLogo.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Svg/main_logo.svg"));
#endif
    }

    public ContentControl ContentControl => Splash;
}
