using System.Collections.Concurrent;
using System.Collections.Immutable;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// Tracks the pre-preview state of fans that have a live preview hold open. A volatile preview actuates the
/// EC without persisting, so if the previewing client disconnects (app crash or kill) before committing
/// (Apply) or restoring, the fan would be stuck on an unapplied preview until the service restarts. The
/// <see cref="FrameworkFanControlGrpcService"/> opens a hold per preview (capturing the pre-preview state),
/// releases it on commit, and reverts to the captured state when the hold's stream breaks uncommitted.
/// </summary>
public sealed partial class FanPreviewWatchdog
{
    // Pre-preview snapshot per fan with an open hold. First hold for a fan wins (captures the applied state).
    private readonly ConcurrentDictionary<int, FanControlStateSnapshot> _holds = new();
    private readonly ILogger<FanPreviewWatchdog> _logger;

    // The logger is optional so the existing parameterless construction in tests keeps working.
    public FanPreviewWatchdog(ILogger<FanPreviewWatchdog>? logger = null)
        => _logger = logger ?? NullLogger<FanPreviewWatchdog>.Instance;

    /// <summary>Records the fan's pre-preview state when a hold opens. No-op if a hold is already tracked.</summary>
    public void Begin(int fanIndex, FanControlStateSnapshot prePreviewState)
    {
        ArgumentNullException.ThrowIfNull(prePreviewState);

        // A hold left open is a fan physically overridden with nothing persisted to explain it, so every
        // transition of this state is traced — the open/close pairing is the whole diagnostic.
        if (_holds.TryAdd(fanIndex, prePreviewState))
        {
            LogHoldOpened(fanIndex, prePreviewState.Mode);
        }
        else
        {
            LogHoldAlreadyOpen(fanIndex);
        }
    }

    /// <summary>
    /// Drops a fan's hold without reverting. Called when the preview is committed (an Apply / persisting
    /// command arrives) or the client restores on its own, so a subsequent hold close does not double-revert.
    /// </summary>
    public void Release(int fanIndex)
    {
        if (_holds.TryRemove(fanIndex, out _))
        {
            LogHoldReleased(fanIndex);
        }
    }

    /// <summary>
    /// Drops every open hold without reverting, returning the fans that had one. Used by the factory reset:
    /// the reset puts each fan back under EC automatic control, so a hold closing afterwards must NOT revert
    /// its fan to the pre-preview state it captured — that would resurrect exactly what the reset wiped, and
    /// automatic control is a safer end state than any revert target anyway.
    /// </summary>
    public ImmutableArray<int> ReleaseAll()
    {
        ImmutableArray<int> fanIndices = [.. _holds.Keys];
        foreach (var fanIndex in fanIndices)
        {
            _holds.TryRemove(fanIndex, out _);
        }

        if (fanIndices.Length > 0)
        {
            LogAllHoldsReleased(fanIndices.Length);
        }

        return fanIndices;
    }

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
    {
        if (!_holds.TryRemove(fanIndex, out prePreviewState!))
        {
            return false;
        }

        // This is the watchdog actually doing its job: a previewing client vanished without committing.
        // Information, not Trace — an uncommitted preview being cleaned up is worth seeing in a normal log.
        LogHoldRevertClaimed(fanIndex, prePreviewState.Mode);
        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Preview hold opened for fan {FanIndex}; pre-preview mode was {PrePreviewMode}.")]
    private partial void LogHoldOpened(int fanIndex, FanControlMode prePreviewMode);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Fan {FanIndex} already has a preview hold open; keeping the state captured by the first hold.")]
    private partial void LogHoldAlreadyOpen(int fanIndex);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Preview hold released for fan {FanIndex} without reverting; the preview was committed or the client restored it.")]
    private partial void LogHoldReleased(int fanIndex);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Released {FanCount} preview hold(s) without reverting; automatic control supersedes every captured state.")]
    private partial void LogAllHoldsReleased(int fanCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Fan {FanIndex} had an uncommitted preview when its client disconnected; reverting to {PrePreviewMode}.")]
    private partial void LogHoldRevertClaimed(int fanIndex, FanControlMode prePreviewMode);
}
