using FrameworkDotnet.Enums;

namespace SubZeroFramework.Services;

/// <summary>
/// Maps a fan's platform role (<see cref="FrameworkFanName"/>, from the EC) to the cooling <b>function</b> title
/// shown on the Device Capabilities page (e.g. LeftFan → "CPU fan"), with the physical location ("Left fan") as the
/// sub-line. Returns <see langword="null"/> when the function is indeterminate (Unknown / Generic / null), so the UI
/// can fall back to the location label as the title.
/// </summary>
public static class FrameworkFanNameDisplay
{
    // FD0001 (platform-specific enum members) is intentionally suppressed: we translate whatever name the
    // device itself reported, so only the cases valid for the running platform are ever hit; the rest are inert.
#pragma warning disable FD0001
    public static string? ToFunction(FrameworkFanName? fanName) => fanName switch
    {
        // Framework 12 / 13 / Desktop slot 0 cools the APU (the CPU package).
        FrameworkFanName.ApuFan => "CPU fan",
        // Framework 16: the left fan sits over the CPU heatpipe, the right fan over the GPU side.
        FrameworkFanName.LeftFan => "CPU fan",
        FrameworkFanName.RightFan => "GPU fan",
        // Framework Desktop chassis fans.
        FrameworkFanName.FrontFan => "System fan",
        FrameworkFanName.ThirdFan => "System fan",
        _ => null,
    };
#pragma warning restore FD0001
}
