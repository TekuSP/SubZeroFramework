namespace SubZeroFramework.Services;

/// <summary>
/// How often telemetry is allowed to reach the UI, independent of how often the service polls.
/// </summary>
/// <remarks>
/// The service's poll interval is user-configurable and can be set far below the rate a human can read, so a
/// subscription that inherits it does UI work at whatever cadence the user picked. These limits cap that:
/// the stream still carries every value, the UI just stops being asked to redraw more often than it is worth
/// redrawing.
///
/// Which operator to use depends on the stream's shape, and the choice is a correctness matter rather than a
/// preference:
///
/// <list type="bullet">
/// <item>
/// Snapshot streams (<c>IObservable&lt;T&gt;</c>) carry the whole current value each time, so dropping
/// intermediate items loses nothing. Use <c>Sample</c>.
/// </item>
/// <item>
/// Change-set streams (<c>IObservable&lt;IChangeSet&lt;T, TKey&gt;&gt;</c>) carry DELTAS. Sampling one would
/// permanently lose whichever adds and removes fell between ticks. Use DynamicData's <c>Batch</c>, which
/// coalesces rather than drops — this is why DynamicData ships no Sample/Throttle for change sets.
/// </item>
/// </list>
///
/// Apply the limit BEFORE <c>ObserveOn</c>, so the coalescing happens off the UI thread and only the
/// surviving value is marshalled onto it. Applying it afterwards still marshals every tick, which is most of
/// the cost.
/// </remarks>
public static class TelemetryRateLimits
{
    /// <summary>
    /// Cadence for live readouts — temperatures, fan speeds, power, status. Four updates a second is past the
    /// point of being readable as changing numbers, and it bounds the worst case if the poll interval is set
    /// to milliseconds.
    /// </summary>
    /// <remarks>
    /// Note that this is a ceiling, not a floor: Rx's Sample and DynamicData's Batch only emit when something
    /// actually arrived in the window, so a one-second poll still updates once a second rather than four times.
    /// </remarks>
    public static readonly TimeSpan LiveReadout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Cadence for inventory-shaped data that changes rarely but arrives on the same poll — hardware info,
    /// module inventory, capabilities. Coalescing harder costs nothing when the content is usually identical.
    /// </summary>
    public static readonly TimeSpan Inventory = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Cadence for chart history series. These re-emit an entire window on every append, so they are the most
    /// expensive streams to process and the least sensitive to a small delay.
    /// </summary>
    public static readonly TimeSpan History = TimeSpan.FromMilliseconds(500);
}
