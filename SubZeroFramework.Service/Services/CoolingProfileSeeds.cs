using System.Collections.Immutable;

using FrameworkDotnet.Enums;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>The profile a fresh install starts with.</summary>
/// <remarks>
/// EXACTLY ONE, and it describes what the machine is already doing: every fan on Auto. A shelf that arrives
/// pre-stocked with somebody else's idea of Quiet and Gaming asks the user to curate a list they did not
/// write, and makes the plus card look like an afterthought rather than the way profiles are meant to be
/// made. One baseline gives the feature something to be, and everything after it is the user's.
/// </remarks>
public static class CoolingProfileSeeds
{
    /// <summary>
    /// The icon the baseline profile carries.
    /// </summary>
    /// <remarks>
    /// A plain string because the service draws nothing and must not reference a UI package; it is a member
    /// name of WinUI's <c>Symbol</c> enum. ROTATE rather than a fan, because the Fluent set has no fan — this
    /// is the circular-motion glyph, which is the nearest thing in it to a spinning blade.
    /// </remarks>
    private const string DefaultIconName = "Rotate";

    public static ImmutableArray<CoolingProfile> Build(IReadOnlyCollection<int> fanIndices)
    {
        ArgumentNullException.ThrowIfNull(fanIndices);

        return
        [
            new CoolingProfile
            {
                Id = "seed-default",
                Name = "Default",
                IconName = DefaultIconName,

                // NO TINT. Black is the shell's resting state, and the baseline profile should look like the
                // machine at rest rather than like a colour someone chose.
                AccentColorArgb = null,
                IsSeeded = true,
                Fans = [.. fanIndices.Select(static index => new CoolingProfileFanEntry
                {
                    FanIndex = index,
                    Mode = FanControlMode.Auto,
                })],
            },
        ];
    }
}
