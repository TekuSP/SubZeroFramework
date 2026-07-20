namespace SubZeroFramework.Services.Navigation;

/// <summary>
/// Maps a top-level navigation region (keyed by the rail item's <c>Tag</c>) to a live accessor for that
/// page's unsaved-changes guard, so the shell can warn before navigating away. Top-level pages are cached
/// (Uno's Visibility navigator), so each registers once; the accessor reads the page's CURRENT guard at
/// query time (e.g. the active Settings section, which changes as the user switches sections).
/// </summary>
public sealed class NavigationGuardRegistry
{
    private readonly Dictionary<string, Func<IUnsavedChangesGuard?>> _guards = new(StringComparer.Ordinal);

    /// <summary>Registers a page's guard accessor under its rail <c>Tag</c> (idempotent; last registration wins).</summary>
    public void Register(string regionKey, Func<IUnsavedChangesGuard?> guardAccessor)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionKey);
        ArgumentNullException.ThrowIfNull(guardAccessor);
        _guards[regionKey] = guardAccessor;
    }

    /// <summary>The guard for the region being left, or null when the region is unregistered/clean-only.</summary>
    public IUnsavedChangesGuard? ResolveGuard(string? regionKey)
        => regionKey is not null && _guards.TryGetValue(regionKey, out var accessor) ? accessor() : null;
}
