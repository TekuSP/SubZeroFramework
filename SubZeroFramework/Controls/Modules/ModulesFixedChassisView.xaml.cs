using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Presentation.MenuItems.Modules;

namespace SubZeroFramework.Controls.Modules;

/// <summary>
/// Shared chassis map for the non-modular laptops (FW12 / FW13 / FW13 Pro): expansion slots around the chassis
/// art, the selected-expansion hero, internal chips and the fixed input cover with its hero. The thin
/// per-platform views provide the chassis/cover art and the page model.
/// </summary>
public sealed partial class ModulesFixedChassisView : UserControl
{
    public ModulesFixedChassisView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page),
        typeof(ModulesModel),
        typeof(ModulesFixedChassisView),
        new PropertyMetadata(null));

    /// <summary>The live Modules page model (via the layout body's accessor bridge).</summary>
    public ModulesModel? Page
    {
        get => (ModulesModel?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public static readonly DependencyProperty ChassisSourceProperty = DependencyProperty.Register(
        nameof(ChassisSource),
        typeof(ImageSource),
        typeof(ModulesFixedChassisView),
        new PropertyMetadata(null));

    /// <summary>The top-down chassis art for this platform.</summary>
    public ImageSource? ChassisSource
    {
        get => (ImageSource?)GetValue(ChassisSourceProperty);
        set => SetValue(ChassisSourceProperty, value);
    }

    public static readonly DependencyProperty CoverSourceProperty = DependencyProperty.Register(
        nameof(CoverSource),
        typeof(ImageSource),
        typeof(ModulesFixedChassisView),
        new PropertyMetadata(null));

    /// <summary>The fixed input cover (keyboard) art for this platform.</summary>
    public ImageSource? CoverSource
    {
        get => (ImageSource?)GetValue(CoverSourceProperty);
        set => SetValue(CoverSourceProperty, value);
    }

    private void OnSlotCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as ModuleSlotCardView)?.Model is { } model)
        {
            Page?.SelectSlot(model);
        }
    }
}
