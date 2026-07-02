using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Controls.Modules.Models;

namespace SubZeroFramework.Controls.Modules;

/// <summary>One FW16 input-deck module tile (PNG art card). Consumers handle <c>Tapped</c> to drive selection.</summary>
public sealed partial class ModuleDeckCardView : UserControl
{
    public ModuleDeckCardView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(ModuleDeckCardModel),
        typeof(ModuleDeckCardView),
        new PropertyMetadata(null));

    public ModuleDeckCardModel? Model
    {
        get => (ModuleDeckCardModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
