#nullable enable
using System.Collections.Generic;
using Marten.Events.Dcb;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    // #5276: the rows FetchForWritingByTags captured are held here rather than queued into the unit
    // of work as they are read. The DCB boundary is a condition on an *append* -- a session that
    // reads a boundary, decides there is nothing to do, and saves has written nothing for the
    // condition to guard, so checking mt_dcb_tag_version for it would both fail a second no-op save
    // of the same boundary and hold row locks a concurrent session with events is waiting on.
    //
    // #5280: holding the rows rather than one operation per fetch is also what lets a save that read
    // the same row through two boundaries assert it once.
    private List<DcbTagVersionEntry>? _capturedDcbRows;

    // #5887: the producer-side bumps for the tags this save's events carry, collected across every
    // stream in the save. They go into the SAME operation as the boundary check, so every
    // mt_dcb_tag_version row the save touches is locked in one sorted pass. Two separately sorted
    // passes -- check first, bumps second -- let a save that checks row c and bumps row w deadlock
    // against one that checks w and bumps c.
    private List<(string TagTable, string TagValue)>? _dcbVersionBumps;
    private HashSet<(string TagTable, string TagValue)>? _dcbVersionBumpKeys;

    internal void CaptureDcbBoundary(IReadOnlyList<DcbTagVersionEntry> rows)
    {
        _capturedDcbRows ??= new List<DcbTagVersionEntry>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            _capturedDcbRows.Add(rows[i]);
        }
    }

    internal void CollectDcbVersionBump(string tagTable, string tagValue)
    {
        _dcbVersionBumpKeys ??= new HashSet<(string TagTable, string TagValue)>();
        _dcbVersionBumps ??= new List<(string TagTable, string TagValue)>();

        if (_dcbVersionBumpKeys.Add((tagTable, tagValue)))
        {
            _dcbVersionBumps.Add((tagTable, tagValue));
        }
    }

    /// <summary>
    /// Queue the one operation that maintains mt_dcb_tag_version for this save: the check of every
    /// boundary this session has read (provided the save has events to append) together with the
    /// producer-side bump of every tag the appended events carry. Called by
    /// <see cref="Marten.Events.EventGraph.ProcessEventsAsync" /> after the event appender has run.
    /// </summary>
    internal void QueueDcbTagVersionOperations()
    {
        var bumps = _dcbVersionBumps;
        var hasBumps = bumps is { Count: > 0 };

        // The overwhelming majority of saves never touch a DCB boundary or a tagged event, so this
        // returns before looking at the streams at all.
        if (_capturedDcbRows is not { Count: > 0 } && !hasBumps) return;

        var asserted = _capturedDcbRows is { Count: > 0 } && appendsEvents()
            ? _capturedDcbRows.ToArray()
            : null;

        if (asserted != null)
        {
            // The assertion sorts and de-duplicates the rows itself -- see #5280 -- and takes over
            // the bumps so all of the rows are locked in one order.
            QueueOperation(new DcbTagVersionAssertion(Options.EventGraph, asserted,
                hasBumps ? bumps!.ToArray() : []));
            _capturedDcbRows!.Clear();
        }
        else if (hasBumps)
        {
            QueueOperation(new DcbTagVersionBumpOperation(Options.EventGraph, bumps!.ToArray()));
        }

        forgetDcbVersionBumps();
    }

    private bool appendsEvents()
    {
        foreach (var stream in _workTracker.Streams)
        {
            if (stream.Events.Count > 0) return true;
        }

        return false;
    }

    private void forgetDcbVersionBumps()
    {
        _dcbVersionBumps?.Clear();
        _dcbVersionBumpKeys?.Clear();
    }

    /// <summary>
    /// Drop any boundary the session read but never appended to. A commit ends the unit of work the
    /// boundary was read for, so it must not outlive it: the retry idiom re-fetches the boundary
    /// after a no-op save, and the re-read row would otherwise be asserted twice.
    /// </summary>
    internal void ForgetPendingDcbBoundaryAssertions()
    {
        _capturedDcbRows?.Clear();
        forgetDcbVersionBumps();
    }
}
