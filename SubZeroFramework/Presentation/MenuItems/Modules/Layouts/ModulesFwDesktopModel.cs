using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

using SubZeroFramework.Controls.Modules;

namespace SubZeroFramework.Presentation.MenuItems.Modules.Layouts;

/// <summary>One fixed rear-I/O port on Framework Desktop — a static per-platform catalog entry (the rear ports
/// are soldered and not EC-enumerable, like the fan advanced-info catalogs).</summary>
public sealed record ModulesRearPortInfo(
    string Name,
    string IconName,
    string Standard,
    string Phy,
    string Features,
    string Security)
{
    public MaterialIconKind IconKindResolved => ModuleArt.ResolveIcon(IconName);
}

/// <summary>Body ViewModel for the Framework Desktop modules layout route (accessor bridge, see ModulesAccessor).</summary>
public sealed partial class ModulesFwDesktopModel : ObservableObject
{
    public ModulesFwDesktopModel(ModulesAccessor accessor)
    {
        Page = accessor.Current
            ?? throw new InvalidOperationException("The Modules page model must exist before its layout body navigates.");
        SelectedRearPort = RearPorts[0];
    }

    public ModulesModel Page { get; }

    /// <summary>The fixed rear I/O, per the Framework Desktop board spec (static catalog; "—" = not documented).</summary>
    public IReadOnlyList<ModulesRearPortInfo> RearPorts { get; } =
    [
        new("HDMI 2.1", "VideoInputHdmi", "HDMI 2.1", "FRL8 with DSC 1.2a", "VRR · Full HDR", "HDCP 2.3"),
        new("USB4", "UsbCPort", "USB4", "40 Gbps", "PD · DP alt-mode", "—"),
        new("USB4", "UsbCPort", "USB4", "40 Gbps", "PD · DP alt-mode", "—"),
        new("DisplayPort 2.1", "Monitor", "DisplayPort 2.1", "—", "—", "—"),
        new("DisplayPort 2.1", "Monitor", "DisplayPort 2.1", "—", "—", "—"),
        new("USB-A", "UsbPort", "USB-A 3.2 Gen 2", "10 Gbps", "—", "—"),
        new("USB-A", "UsbPort", "USB-A 3.2 Gen 2", "10 Gbps", "—", "—"),
        new("Audio", "Headphones", "3.5 mm combo", "—", "—", "—"),
        new("5G LAN", "Ethernet", "RJ45 5 GbE", "—", "—", "—"),
    ];

    [ObservableProperty]
    public partial ModulesRearPortInfo? SelectedRearPort { get; set; }
}
