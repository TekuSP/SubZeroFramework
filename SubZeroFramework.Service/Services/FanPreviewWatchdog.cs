using System.Collections.Concurrent;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Tracks the pre-preview state of fans that have a live preview hold open. A volatile preview actuates the
/// EC without persisting, so if the previewing client disconnects (app crash or kill) before committing
/// (Apply) or restoring, the fan would be stuck on an unapplied preview until the service restarts. The
/// <see cref="FrameworkFanControlGrpcService"/> opens a hold per preview (capturing the pre-preview state),
/// releases it on commit, and reverts to the captured state when the hold's stream breaks uncommitted.
/// </summary>
public sealed class FanPreviewWatchdog
{
    // Pre-preview snapshot per fan with an open hold. First hold for a fan wins (captures the applied state).
    private readonly ConcurrentDictionary<int, FanControlStateSnapshot> _holds = new();

    /// <summary>Records the fan's pre-preview state when a hold opens. No-op if a hold is already tracked.</summary>
    public void Begin(int fanIndex, FanControlStateSnapshot prePreviewState)
    {
        ArgumentNullException.ThrowIfNull(prePreviewState);
        _holds.TryAdd(fanIndex, prePreviewState);
    }

    /// <summary>
    /// Drops a fan's hold without reverting. Called when the preview is committed (an Apply / persisting
    /// command arrives) or the client restores on its own, so a subsequent hold close does not double-revert.
    /// </summary>
    public void Release(int fanIndex) => _holds.TryRemove(fanIndex, out _);

    /// <summary>
    /// Whether the fan currently has a live preview hold open. Commands that would persist the fan's
    /// in-memory state (which reflects the volatile preview) without meaning to commit it check this first.
    /// </summary>
    public bool HasOpenHold(int fanIndex) => _holds.ContainsKey(fanIndex);

    /// <summary>
    /// Atomically takes a fan's captured pre-preview state for reverting. Returns false when the hold was
    /// already released (committed / restored) — in which case the caller must not revert.
    /// </summary>
    public bool TryTakeForRevert(int fanIndex, out FanControlStateSnapshot prePreviewState)
        => _holds.TryRemove(fanIndex, out prePreviewState!);
}
