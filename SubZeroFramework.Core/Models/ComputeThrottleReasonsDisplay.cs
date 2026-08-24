namespace SubZeroFramework.Models;

/// <summary>
/// Turns a throttle-reason bitmask into the sentence a user reads.
/// </summary>
/// <remarks>
/// In Core rather than beside the card that renders it so the wording is testable, and so the app and any
/// future surface describe the same state the same way.
/// </remarks>
public static class ComputeThrottleReasonsDisplay
{
    /// <summary>Shown when the source could not be asked at all.</summary>
    public const string Unknown = "--";

    /// <summary>Shown when the source answered and nothing is holding the clocks back.</summary>
    public const string NotThrottled = "Running at full speed";

    /// <summary>
    /// Describes every reason currently asserted, most actionable first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null and <see cref="ComputeThrottleReasons.None"/> deliberately read differently: "--" means the device
    /// could not be asked, <see cref="NotThrottled"/> means it answered and is running free. Collapsing the
    /// two would turn "we do not know" into a reassurance.
    /// </para>
    /// <para>
    /// EVERY asserted reason is listed rather than just the first. On the reference RTX 5070 the power limit
    /// is asserted permanently — even at idle at 800 MHz — so showing one reason would read "Power limit"
    /// forever and hide a thermal limit appearing alongside it, which is the one more airflow fixes.
    /// Temperature is ordered first for the same reason.
    /// </para>
    /// </remarks>
    public static string Describe(ComputeThrottleReasons? reasons)
    {
        if (reasons is not { } value)
        {
            return Unknown;
        }

        if (value == ComputeThrottleReasons.None)
        {
            return NotThrottled;
        }

        List<string> parts = [];

        if (value.HasFlag(ComputeThrottleReasons.ThermalLimit))
        {
            parts.Add("Temperature");
        }

        if (value.HasFlag(ComputeThrottleReasons.PowerLimit))
        {
            parts.Add("Power limit");
        }

        if (value.HasFlag(ComputeThrottleReasons.ApplicationLimit))
        {
            parts.Add("Applied limit");
        }

        if (value.HasFlag(ComputeThrottleReasons.Idle))
        {
            parts.Add("Idle");
        }

        if (value.HasFlag(ComputeThrottleReasons.Other))
        {
            parts.Add("Other");
        }

        // A bitmask carrying only bits this model does not name still means the device IS throttled, so it
        // must not fall through to "running at full speed".
        return parts.Count > 0 ? string.Join(", ", parts) : "Other";
    }
}
