using System.Collections.Immutable;

namespace SubZeroFramework.Services;

/// <summary>
/// A bounded, in-memory record of everything a process has logged since it started, so its own logs can be
/// shown in the app without the user hunting through Event Viewer or journalctl.
/// </summary>
/// <remarks>
/// Used by BOTH sides. The service fills one so the app can fetch it over gRPC; the app fills another for its
/// own client-side records, which otherwise reach nobody — the desktop head is a GUI-subsystem binary, so a
/// console sink writes to a console that does not exist.
///
/// Deliberately BOUNDED: a service that runs for weeks must not grow a log buffer without limit, so the
/// oldest entry is dropped once <see cref="Capacity"/> is reached. Callers are told how many entries were
/// dropped, because "the last N lines" and "everything since start" are different claims and the UI must not
/// make the stronger one. A restart empties it — which is honest, the process really did restart.
/// </remarks>
public sealed class InMemoryLogBuffer
{
    /// <summary>
    /// Entries retained. A few thousand covers a long troubleshooting session while staying a few MB at worst;
    /// the service's whole buffer is serialized in one gRPC reply, so this also bounds that message.
    /// </summary>
    public const int Capacity = 2000;

    private readonly Lock _gate = new();
    private readonly Queue<ServiceLogEntry> _entries = new(Capacity);
    private long _droppedCount;

    /// <summary>Records one entry, evicting the oldest when full.</summary>
    public void Add(ServiceLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            if (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
                _droppedCount++;
            }

            _entries.Enqueue(entry);
        }
    }

    /// <summary>Everything currently retained, oldest first, plus how many older entries were dropped.</summary>
    public (ImmutableArray<ServiceLogEntry> Entries, long DroppedCount) Snapshot()
    {
        lock (_gate)
        {
            return ([.. _entries], _droppedCount);
        }
    }

    /// <summary>Empties the buffer (the app's "clear" action). The dropped counter restarts with it.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _droppedCount = 0;
        }
    }
}
