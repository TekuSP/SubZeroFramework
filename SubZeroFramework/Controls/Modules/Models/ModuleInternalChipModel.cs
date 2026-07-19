using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Services;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Modules.Models;

/// <summary>
/// One fixed internal device chip on the Modules page (webcam / fingerprint reader / touchscreen …):
/// icon + name + an Enabled/Disabled state line colored by state.
/// </summary>
public partial class ModuleInternalChipModel : ObservableObject
{
    public ModuleInternalChipModel(FrameworkModuleIdentity identity)
    {
        Identity = identity;
    }

    public FrameworkModuleIdentity Identity { get; }

    // The descriptor is the only mutable input; assigning it re-raises the state-derived displays. Name and
    // IconKind derive solely from the immutable Identity, so they never change after construction.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateDisplay))]
    [NotifyPropertyChangedFor(nameof(StateBrush))]
    public partial ModuleDescriptorStatus? Descriptor { get; private set; }

    public string Name => FrameworkModuleDisplay.For(Identity).DisplayName;

    public MaterialIconKind IconKind => ModuleArt.ResolveIcon(FrameworkModuleDisplay.For(Identity).IconName);

    public string StateDisplay => Descriptor is { } descriptor
        ? FrameworkModuleDisplay.StateLabel(descriptor.Flags)
        : "Unknown";

    public Brush StateBrush => StateDisplay is "Disconnected" or "Unknown"
        ? AppThemeBrushes.Get("StatusWarningBrush", AppThemeBrushes.StatusWarningColor)
        : AppThemeBrushes.Get("StatusSuccessBrush", AppThemeBrushes.StatusSuccessColor);

    public void Update(ModuleDescriptorStatus descriptor) => Descriptor = descriptor;
}
