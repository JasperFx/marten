#nullable enable
using System.Collections.Generic;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    // #5276: boundary assertions captured by FetchForWritingByTags are held here rather than being
    // queued straight into the unit of work. The DCB boundary is a condition on an *append* -- a
    // session that reads a boundary, decides there is nothing to do, and saves has written nothing
    // for the condition to guard, so bumping mt_dcb_tag_version for it would both fail a second
    // no-op save of the same boundary and invalidate a concurrent session that does have events.
    //
    // EventGraph.ProcessEventsAsync drains this list into the unit of work only when the save
    // actually appends events, and it drains it *before* the appender queues its own producer-side
    // DcbTagVersionBumpOperations so the assertion still reads the pre-bump version.
    private List<Weasel.Storage.IStorageOperation>? _pendingDcbAssertions;

    internal void RegisterDcbBoundaryAssertion(Weasel.Storage.IStorageOperation assertion)
    {
        (_pendingDcbAssertions ??= new List<Weasel.Storage.IStorageOperation>()).Add(assertion);
    }

    /// <summary>
    /// Move every boundary assertion captured since the last commit into the unit of work, provided
    /// this save has events to append. Called by
    /// <see cref="Marten.Events.EventGraph.ProcessEventsAsync" />.
    /// </summary>
    internal void QueuePendingDcbBoundaryAssertions()
    {
        // The overwhelming majority of saves never touch a DCB boundary, so this returns before
        // looking at the streams at all.
        if (_pendingDcbAssertions is not { Count: > 0 }) return;

        var appendsEvents = false;
        foreach (var stream in _workTracker.Streams)
        {
            if (stream.Events.Count == 0) continue;
            appendsEvents = true;
            break;
        }

        if (!appendsEvents) return;

        foreach (var assertion in _pendingDcbAssertions)
        {
            QueueOperation(assertion);
        }

        _pendingDcbAssertions.Clear();
    }

    /// <summary>
    /// Drop any boundary the session read but never appended to. A commit ends the unit of work the
    /// boundary was read for, so it must not outlive it: the retry idiom re-fetches the boundary
    /// after a no-op save, and two assertions over one tag row with the same captured version would
    /// have the second one fail against the first one's bump.
    /// </summary>
    internal void ForgetPendingDcbBoundaryAssertions()
    {
        _pendingDcbAssertions?.Clear();
    }
}
