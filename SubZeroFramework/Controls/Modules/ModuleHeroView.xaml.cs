using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using SubZeroFramework.Controls.Modules.Models;

namespace SubZeroFramework.Controls.Modules;

/// <summary>
/// The shared selected-module hero band (mockup): icon/brand logo, title, confidence chip, flag pills, an
/// optional power-delivery pill and adaptive stat tiles. Renders whatever <see cref="Source"/> snapshot it is
/// given (expansion-card slots and input-deck modules build their own), collapsing entirely while null.
/// </summary>
public sealed partial class ModuleHeroView : UserControl
{
    public ModuleHeroView()
    {
        this.InitializeComponent();
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ModuleHeroModel),
        typeof(ModuleHeroView),
        new PropertyMetadata(null, static (sender, args) =>
            ((ModuleHeroView)sender).RootBorder.Visibility = args.NewValue is null ? Visibility.Collapsed : Visibility.Visible));

    /// <summary>The hero snapshot to render; null collapses the band.</summary>
    public ModuleHeroModel? Source
    {
        get => (ModuleHeroModel?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
}
