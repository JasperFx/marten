using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Operations;
using Marten.Events.Projections;
using Marten.Internal.Sessions;

namespace Marten.Events;

internal class QuickEventAppender: IEventAppender
{
    // #4294 Lens-3 Row 3: PrepareEvents takes a Queue<long> that only the Rich
    // metadata branch (applyRichMetadata) consumes. QuickEventAppender is only
    // reached when AppendMode == Quick, at which point PrepareEvents routes to
    // applyQuickMetadata, which never touches the queue. Replace the per-save
    // allocation with a process-wide unused sentinel. Static readonly so even
    // if the invariant ever breaks the shared instance is at least obvious in
    // diffs / crash dumps.
    private static readonly Queue<long> _unusedSequencesSentinel = new();

    private static void registerOperationsForStreams(EventGraph eventGraph, DocumentSessionBase session)
    {
        var storage = session.EventStorage();

        // Queue AssertStreamVersion operations for streams with AlwaysEnforceConsistency but no events
        foreach (var stream in session.WorkTracker.Streams.Where(x => !x.Events.Any() && x.AlwaysEnforceConsistency && x.ExpectedVersionOnServer.HasValue))
        {
            stream.TenantId ??= session.TenantId;

            // The closed-shape storage picks the single-tenant vs conjoined
            // (and Guid vs string identity) operation variant — no per-op
            // tenancy/identity branching here. See #4803.
            session.QueueOperation(storage.AssertStreamVersion(stream));
        }

        // See _unusedSequencesSentinel comment: the queue parameter on
        // PrepareEvents is dead in this code path; share a single instance
        // across all saves rather than allocating per-save.
        var sequences = _unusedSequencesSentinel;

        foreach (var stream in session.WorkTracker.Streams.Where(x => x.Events.Any()))
        {
            stream.TenantId ??= session.TenantId;
            // #4424: see TenantPropagation. Pass a metadata context whose
            // TenantId matches the stream so applyQuickMetadata stamps events
            // with the stream's tenant instead of the session's.
            var metadataContext = TenantPropagation.MetadataContextFor(session, stream);

            // #4596 Phase 1 Session 2 / #4614 Session 4: when per-tenant
            // partitioning is on, the per-event `QuickAppendEventWithVersion`
            // INSERT would inline `nextval('mt_events_sequence')` — the *global*
            // sequence — via SequenceColumn.ValueSql. Only the bulk
            // `mt_quick_append_events` function honors the per-tenant sequence
            // pick, so partitioning forces the bulk function for every shape.
            var forceBulkFunction = eventGraph.UseTenantPartitionedEvents;

            if (!forceBulkFunction && stream.ActionType == StreamActionType.Start)
            {
                // New-stream StartStream stays on the per-event InsertStream +
                // QuickAppendEventWithVersion route. This is deliberate and NOT
                // affected by #4765:
                //   * The InsertStream's mt_streams PK violation is what surfaces
                //     ExistingStreamIdCollisionException when an id is reused
                //     (including reuse of a previously-archived stream id) — the
                //     bulk function can't distinguish that case. It also routes
                //     the insert to mt_streams_default so UseArchivedStreamPartitioning
                //     reuse semantics are preserved.
                //   * There is no deterministic sequence-gap hazard here: a
                //     colliding StartStream fails on the InsertStream (queued
                //     first in the batch) before any per-event nextval runs, so
                //     the loser advances no sequence.
                stream.PrepareEvents(0, eventGraph, sequences, metadataContext);
                session.QueueOperation(storage.InsertStream(stream));

                foreach (var @event in stream.Events)
                {
                    session.QueueOperation(storage.QuickAppendEventWithVersion(stream, @event));
                }

                // Individual inserts don't use the function, so queue separate tag operations
                EventTagOperations.QueueTagOperationsByEventId(eventGraph, session, stream);
            }
            else
            {
                // #4765: everything else — plain appends AND the optimistic
                // shapes (FetchForWriting / AppendOptimistic / AppendExclusive /
                // expected-version Append) — routes through the bulk
                // `mt_quick_append_events` function. This is the fix: the
                // ExpectedVersionOnServer shape used to take a per-event
                // UpdateStreamVersion + N × QuickAppendEventWithVersion route
                // whose OCC check was C#-side (RecordsAffected). On a concurrent
                // OCC loser, the per-event INSERT still fired
                // nextval('mt_events_sequence') before raising 23505 on the
                // events PK — and nextval is non-transactional, so the sequence
                // stayed advanced past the highest committed seq_id, leaving a
                // permanent gap that stalls the async daemon's high-water
                // detector forever (#4749). The bulk function checks the version
                // via the trailing `expected_version` parameter (MT003 on
                // mismatch, translated to EventStreamUnexpectedMaxEventIdException
                // by QuickAppendEventsOperationBase.TryTransform — the same
                // exception type the rich path's UpdateStreamVersion throws)
                // *before* the foreach that calls nextval, so an OCC loss
                // advances no sequence.
                //
                // The per-event QuickAppendEventWithVersion op is NOT dead — the
                // daemon's projection side-effect emission (JasperFx EventSlice ->
                // IProjectionBatch.QuickAppendEventWithVersion, #4428) and the
                // StartStream branch above both still use it.
                //
                // Tags in TagTables mode are written inside the PostgreSQL
                // function via array parameters. In HStore mode the function
                // signature is trimmed (no per-tag varchar[] params), so tags are
                // written via a follow-up UPDATE keyed on the event's id.
                //
                // PrepareEvents gets ExpectedVersionOnServer (or 0) as the current
                // version so the client-side optimistic-concurrency guard
                // (StreamAction.PrepareEvents in jasperfx) doesn't false-positive
                // on "expected N but was 0" — events get their server-bound
                // versions from ExpectedVersionOnServer + 1. The authoritative
                // version check still happens server-side.
                var preparedFrom = stream.ExpectedVersionOnServer ?? 0L;
                stream.PrepareEvents(preparedFrom, eventGraph, sequences, metadataContext);
                var quickAppendEvents = (QuickAppendEventsOperationBase)storage.QuickAppendEvents(stream);
                quickAppendEvents.Events = eventGraph;
                session.QueueOperation(quickAppendEvents);

                if (eventGraph.DcbStorageMode == DcbStorageMode.HStore)
                {
                    EventTagOperations.QueueTagOperationsByEventId(eventGraph, session, stream);
                }
            }

            // #4591: queue the DCB tag-version producer-bump once per stream,
            // regardless of which tag-write path above ran. The non-HStore
            // bulk QuickAppend code path writes tags inside the PostgreSQL
            // function — without this hook, those commits would silently
            // bypass any in-flight DCB boundary check.
            EventTagOperations.QueueDcbVersionBumpIfNeeded(eventGraph, session, stream);
        }
    }

    public async Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session, IInlineProjection<IDocumentOperations>[] inlineProjections,
        CancellationToken token)
    {
        registerOperationsForStreams(eventGraph, session);

        // 9.0 (#4306): pass the tracker collection directly now that the
        // IInlineProjection contract takes IEnumerable<StreamAction>.
        // Issue #4481: filter to streams with events. See the matching
        // guard in RichEventAppender.ProcessEventsAsync for context.
        foreach (var projection in inlineProjections)
        {
            await projection.ApplyAsync(session, session.WorkTracker.Streams.Where(x => x.Events.Any()), token).ConfigureAwait(false);
        }
    }
}
