using System.Collections.Immutable;

using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// A bounded, in-memory record of everything the service has logged since it started, so the app can show its
/// logs without the user hunting through Event Viewer or journalctl.
/// </summary>
/// <remarks>
/// Deliberately BOUNDED: a service that runs for weeks must not grow a log buffer without limit, so the
/// oldest entry is dropped once <see cref="Capacity"/> is reached. Clients are told how many entries were
/// dropped, because "the last N lines" and "everything since start" are different claims and the UI must not
/// make the stronger one. A restart empties it — which is honest, the service really did restart.
/// </remarks>
public sealed class InMemoryServiceLogBuffer
{
    /// <summary>
    /// Entries retained. A few thousand covers a long troubleshooting session while staying a few MB at worst;
    /// the whole buffer is serialized in one gRPC reply, so this also bounds that message.
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
