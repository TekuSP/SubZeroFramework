namespace SubZeroFramework.Models;

/// <summary>
/// What a fan actually cools, as opposed to where it physically sits.
/// </summary>
/// <remarks>
/// The EC reports a fan's slot role — "left fan", "right fan" — which says where it is, not what it is for.
/// On a Framework 16 the left fan sits over the CPU heatpipe and the right over the GPU side, and that
/// distinction decides real behaviour: which component a calibration has to heat, and what the UI should call
/// the fan. A position is not a purpose, so the purpose gets its own type.
/// </remarks>
public enum FanCoolingRole
{
    /// <summary>Indeterminate — an unknown platform, or a slot with no assigned role.</summary>
    Unknown = 0,

    /// <summary>Cools the processor package (CPU or APU).</summary>
    Cpu = 1,

    /// <summary>Cools the discrete GPU.</summary>
    Gpu = 2,

    /// <summary>A chassis fan moving air through the whole case rather than over one component.</summary>
    System = 3,
}
