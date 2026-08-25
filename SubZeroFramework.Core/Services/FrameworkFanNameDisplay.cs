using FrameworkDotnet.Enums;

using SubZeroFramework.Models;

namespace SubZeroFramework.Services;

/// <summary>
/// Maps a fan's platform slot role (<see cref="FrameworkFanName"/>, from the EC) to what it actually cools,
/// and to the cooling <b>function</b> title shown on the Device Capabilities page (e.g. LeftFan → "CPU fan"),
/// with the physical location ("Left fan") as the sub-line. Returns <see langword="null"/> when the function
/// is indeterminate (Unknown / Generic / null), so the UI can fall back to the location label as the title.
/// </summary>
public static class FrameworkFanNameDisplay
{
    // FD0001 (platform-specific enum members) is intentionally suppressed: we translate whatever name the
    // device itself reported, so only the cases valid for the running platform are ever hit; the rest are inert.
#pragma warning disable FD0001

    /// <summary>
    /// What this fan cools.
    /// </summary>
    /// <remarks>
    /// The single source of truth for the CPU-versus-GPU distinction. <see cref="ToFunction"/> derives its
    /// label from this, and a calibration derives which component it must heat from it — so the two can never
    /// disagree about what the right fan on a Framework 16 is for.
    /// </remarks>
    public static FanCoolingRole ToRole(FrameworkFanName? fanName) => fanName switch
    {
        // Framework 12 / 13 / Desktop slot 0 cools the APU (the CPU package).
        FrameworkFanName.ApuFan => FanCoolingRole.Cpu,

        // Framework 16: the left fan sits over the CPU heatpipe, the right fan over the GPU side.
        FrameworkFanName.LeftFan => FanCoolingRole.Cpu,
        FrameworkFanName.RightFan => FanCoolingRole.Gpu,

        // Framework Desktop chassis fans move case air rather than cooling one component.
        FrameworkFanName.FrontFan => FanCoolingRole.System,
        FrameworkFanName.ThirdFan => FanCoolingRole.System,
        _ => FanCoolingRole.Unknown,
    };
#pragma warning restore FD0001

    /// <summary>The cooling function title, or null when it cannot be determined.</summary>
    public static string? ToFunction(FrameworkFanName? fanName) => ToFunction(ToRole(fanName));

    /// <summary>The cooling function title for a role, or null when indeterminate.</summary>
    public static string? ToFunction(FanCoolingRole role) => role switch
    {
        FanCoolingRole.Cpu => "CPU fan",
        FanCoolingRole.Gpu => "GPU fan",
        FanCoolingRole.System => "System fan",
        _ => null,
    };

    /// <summary>
    /// A sentence describing what the fan cools, for tooltips and the calibration wizard.
    /// </summary>
    /// <remarks>
    /// The wizard in particular needs to explain why calibrating the right fan on a Framework 16 loads the
    /// GPU rather than the processor — otherwise the machine appears to do nothing for several minutes.
    /// </remarks>
    public static string ToDescription(FanCoolingRole role) => role switch
    {
        FanCoolingRole.Cpu => "Cools the processor package.",
        FanCoolingRole.Gpu => "Cools the discrete graphics module.",
        FanCoolingRole.System => "Moves air through the chassis rather than cooling one component.",
        _ => "This fan's cooling role could not be determined on this platform.",
    };
}
