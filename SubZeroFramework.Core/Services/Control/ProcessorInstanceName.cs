namespace SubZeroFramework.Services.Control;

/// <summary>
/// Parses the instance names of the Windows <c>Processor Information</c> PDH counter set.
/// </summary>
/// <remarks>
/// <para>
/// The wildcard instance returns a mix of real processors and rollups. Real ones are
/// <c>"{group},{processor}"</c> — the counter set is group-aware, which is why it superseded the older
/// <c>Processor</c> set on machines with more than 64 logical processors. Rollups are <c>"_Total"</c> for the
/// machine and <c>"{group},_Total"</c> for each processor group.
/// </para>
/// <para>
/// Getting this filter wrong is not a subtle bug: counting <c>_Total</c> as a core would add a phantom
/// processor whose load is the average of all the others, and on a multi-group machine it would add one per
/// group. Kept cross-platform and separate from the reader so it can be tested without Windows — the reader
/// itself compiles only into the Windows target.
/// </para>
/// </remarks>
public static class ProcessorInstanceName
{
    private const string TotalSuffix = "_Total";

    /// <summary>True for the machine-wide rollup, which carries the aggregate utilisation.</summary>
    public static bool IsMachineTotal(ReadOnlySpan<char> instanceName)
        => instanceName.Equals(TotalSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a real per-processor instance. Returns false for any rollup, so a caller can filter with the
    /// same call that parses.
    /// </summary>
    /// <param name="instanceName">The PDH instance name, e.g. <c>"0,5"</c>.</param>
    /// <param name="group">The processor group the logical processor belongs to.</param>
    /// <param name="processor">The logical processor's index within its group.</param>
    public static bool TryParse(ReadOnlySpan<char> instanceName, out int group, out int processor)
    {
        group = 0;
        processor = 0;

        if (instanceName.IsEmpty || IsMachineTotal(instanceName))
        {
            return false;
        }

        var separator = instanceName.IndexOf(',');
        if (separator < 0)
        {
            // The older, non-group-aware "Processor" counter set names instances by bare index. Accepted so a
            // caller that falls back to that set gets the same parse, in group 0.
            return int.TryParse(instanceName, out processor);
        }

        var groupText = instanceName[..separator];
        var processorText = instanceName[(separator + 1)..];

        // "{group},_Total" is the per-group rollup, and averages every processor in that group.
        if (processorText.Equals(TotalSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(groupText, out group) && int.TryParse(processorText, out processor);
    }

    /// <summary>
    /// A sortable ordinal that keeps groups in order and processors in order within a group, so per-core
    /// readings stay in a stable, meaningful sequence rather than PDH's enumeration order.
    /// </summary>
    public static long ToOrdinal(int group, int processor) => ((long)group << 32) | (uint)processor;
}
