namespace SubZeroFramework.Tests.Windows;

/// <summary>
/// The categories that say WHAT a test needs, so a run on the wrong machine skips rather than fails.
/// </summary>
/// <remarks>
/// <para>
/// Two different requirements get confused constantly, so they are two different categories. Most of what
/// this suite covers reads counters and power meters that ANY machine has — a test for those must not be
/// gated behind owning a Framework laptop, or it never runs anywhere. A much smaller set genuinely talks to
/// Framework's embedded controller, and that one has no substitute.
/// </para>
/// <para>
/// The operating system is a separate axis again, and NUnit already has an attribute for it: put
/// <c>[Platform("Win")]</c> on the fixture rather than inventing a category, so a Linux run reports these as
/// skipped-by-platform instead of silently not existing.
/// </para>
/// </remarks>
public static class HardwareTestCategories
{
    /// <summary>
    /// Reads or loads the real machine, but any machine of the right OS will do.
    /// </summary>
    /// <remarks>
    /// Slow, and a busy machine can fail them — that is a limitation of measuring the real thing, not a
    /// flaky test. Exclude with <c>--filter TestCategory!=Hardware</c>.
    /// </remarks>
    public const string Machine = "Hardware";

    /// <summary>
    /// Needs an actual Framework laptop: reads the embedded controller for fan speed, sensor temperatures or
    /// charger draw, none of which exist to be faked on other hardware.
    /// </summary>
    /// <remarks>
    /// Usually also needs the service's privileges, since EC access is not available to an ordinary user.
    /// Exclude with <c>--filter TestCategory!=FrameworkHardware</c>.
    /// </remarks>
    public const string FrameworkLaptop = "FrameworkHardware";
}
