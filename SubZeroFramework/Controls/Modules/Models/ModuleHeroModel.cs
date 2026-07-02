using Material.Icons;

using Microsoft.UI.Xaml;

namespace SubZeroFramework.Controls.Modules.Models;

/// <summary>One flag chip on a selected-module hero (e.g. "Connected", "PD contract").</summary>
public sealed record ModuleHeroChipModel(MaterialIconKind IconKind, string Text);

/// <summary>One stat tile on a selected-module hero (e.g. "Vendor ID" / "0x32AC").</summary>
public sealed record ModuleHeroTileModel(MaterialIconKind IconKind, string Label, string Value);

/// <summary>
/// Immutable snapshot of everything the shared selected-module hero renders (icon/logo, title, confidence chip,
/// flag chips, optional power-delivery pill, stat tiles). Card models rebuild it whenever their data updates and
/// swap the whole instance, so the hero needs no per-property change tracking.
/// </summary>
public sealed record ModuleHeroModel(
    MaterialIconKind IconKind,
    string? LogoVendor,
    string Title,
    string ConfidenceLabel,
    bool ShowConfidence,
    IReadOnlyList<ModuleHeroChipModel> Chips,
    string? PdPillText,
    IReadOnlyList<ModuleHeroTileModel> Tiles)
{
    public Visibility ConfidenceVisibility => ShowConfidence ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PdPillVisibility => string.IsNullOrEmpty(PdPillText) ? Visibility.Collapsed : Visibility.Visible;
}
