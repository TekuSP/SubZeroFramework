using CommunityToolkit.Mvvm.ComponentModel;

using Material.Icons;

namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// One chip in the redesigned "Applies to" link row, rendered as a <c>ToggleButton</c>: checked = linked
/// (shares this curve), unchecked = available. The currently-edited fan is locked on (pencil icon); stalled
/// fans are disabled because they cannot accept a curve. The icon reflects the chip's role.
/// </summary>
public partial class FanLinkChip : ObservableObject
{
    public FanLinkChip(int fanIndex)
    {
        FanIndex = fanIndex;
    }

    public int FanIndex { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    /// <summary>The fan being edited — locked into the group and rendered with a pencil.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(IsToggleEnabled))]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    public partial bool IsLinked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(IsToggleEnabled))]
    public partial bool IsStalled { get; set; }

    /// <summary>The edited fan and stalled fans cannot be toggled in or out of the group.</summary>
    public bool IsToggleEnabled => !IsCurrent && !IsStalled;

    public MaterialIconKind IconKind => IsStalled
        ? MaterialIconKind.FanOff
        : IsCurrent
            ? MaterialIconKind.Pencil
            : IsLinked
                ? MaterialIconKind.Fan
                : MaterialIconKind.LinkVariant;
}
