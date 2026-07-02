using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Controls.Modules.Models;

namespace SubZeroFramework.Controls.Modules;

/// <summary>One numbered expansion-card slot card on a chassis map. Consumers handle <c>Tapped</c> to drive
/// selection (the model exposes the selection visuals).</summary>
public sealed partial class ModuleSlotCardView : UserControl
{
    public ModuleSlotCardView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(ModuleSlotCardModel),
        typeof(ModuleSlotCardView),
        new PropertyMetadata(null));

    public ModuleSlotCardModel? Model
    {
        get => (ModuleSlotCardModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
}
