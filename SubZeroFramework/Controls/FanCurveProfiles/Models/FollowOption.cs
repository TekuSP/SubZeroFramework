namespace SubZeroFramework.Controls.FanCurveProfiles.Models;

/// <summary>
/// One entry in the "driving source" selector for a curve profile slot: either the fan's own curve
/// (<see cref="FanIndex"/> null) or a follow link to another fan (<see cref="FanIndex"/> set).
/// </summary>
public sealed class FollowOption
{
    public FollowOption(int? fanIndex, string label)
    {
        FanIndex = fanIndex;
        Label = label;
    }

    public int? FanIndex { get; }

    public string Label { get; }
}
