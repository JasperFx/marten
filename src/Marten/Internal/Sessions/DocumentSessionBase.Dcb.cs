#nullable enable
using System.Collections.Generic;
using Marten.Events.Dcb;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    // #5276: the rows FetchForWritingByTags captured are held here rather than queued into the unit
    // of work as they are read. The DCB boundary is a condition on an *append* -- a session that
    // reads a boundary, decides there is nothing to do, and saves has written nothing for the
    // condition to guard, so bumping mt_dcb_tag_version for it would both fail a second no-op save
    // of the same boundary and invalidate a concurrent session that does have events.
    //
    // #5280: holding the rows rather than one operation per fetch is also what lets a save that read
    // the same row through two boundaries assert it once. Two assertions over one row carry the same
    // captured version, and the first one's bump makes the second one's WHERE miss.
    private List<DcbTagVersionEntry>? _capturedDcbRows;

    internal void CaptureDcbBoundary(IReadOnlyList<DcbTagVersionEntry> rows)
    {
        _capturedDcbRows ??= new List<DcbTagVersionEntry>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            _capturedDcbRows.Add(rows[i]);
        }
    }

    /// <summary>
    /// Queue the single assertion covering every boundary this session has read, provided the save
    /// has events to append. Called by <see cref="Marten.Events.EventGraph.ProcessEventsAsync" />.
    /// </summary>
    internal void QueuePendingDcbBoundaryAssertions()
    {
        // The overwhelming majority of saves never touch a DCB boundary, so this returns before
        // looking at the streams at all.
        if (_capturedDcbRows is not { Count: > 0 }) return;

        var appendsEvents = false;
        foreach (var stream in _workTracker.Streams)
        {
            if (stream.Events.Count == 0) continue;
            appendsEvents = true;
            break;
        }

        if (!appendsEvents) return;

        // The assertion sorts and de-duplicates the rows itself -- see #5280.
        QueueOperation(new DcbTagVersionAssertion(Options.EventGraph, _capturedDcbRows.ToArray()));
        _capturedDcbRows.Clear();
    }

    /// <summary>
    /// Drop any boundary the session read but never appended to. A commit ends the unit of work the
    /// boundary was read for, so it must not outlive it: the retry idiom re-fetches the boundary
    /// after a no-op save, and the re-read row would otherwise be asserted twice.
    /// </summary>
    internal void ForgetPendingDcbBoundaryAssertions()
    {
        _capturedDcbRows?.Clear();
    }
}
