using Material.Icons;

using Microsoft.UI.Xaml.Media;

namespace SubZeroFramework.Controls.DeviceCapabilities.Models;

/// <summary>One extra icon+text line on an onboard-device tile (e.g. the battery's "95.5% health").</summary>
public sealed record DeviceCapabilitiesTileLineModel(MaterialIconKind IconKind, string Text, Brush Brush);
