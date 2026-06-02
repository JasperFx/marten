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

            if (stream.Key != null)
            {
                session.QueueOperation(new AssertStreamVersionByKey(eventGraph, stream));
            }
            else
            {
                session.QueueOperation(new AssertStreamVersionById(eventGraph, stream));
            }
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

            // #4596 Phase 1 Session 2: when per-tenant partitioning is on, the
            // per-event `QuickAppendEventWithVersion` INSERT (used by the Start
            // and ExpectedVersionOnServer paths) would inline
            // `nextval('mt_events_sequence')` — the *global* sequence — via
            // SequenceColumn.ValueSql. Only the bulk `mt_quick_append_events`
            // function honors the per-tenant sequence pick (#4596 Session 2
            // rewrite). Route every per-tenant append through the bulk function
            // so the seq_id always comes from the tenant's
            // `mt_events_sequence_{suffix}`. The function handles new-stream
            // creation internally and rejects unregistered tenants with
            // SQLSTATE MT002. Optimistic-concurrency (ExpectedVersionOnServer)
            // appends with per-tenant partitioning are not supported yet —
            // Session 3's progression rework lays the groundwork, Session 4
            // wires the function signature for it.
            var forceBulkFunction = eventGraph.UseTenantPartitionedEvents;
            if (forceBulkFunction && stream.ExpectedVersionOnServer.HasValue)
            {
                throw new NotSupportedException(
                    $"Optimistic-concurrency event appends with explicit ExpectedVersionOnServer are not yet " +
                    $"supported when Events.UseTenantPartitionedEvents is on (stream id '{stream.Id}'). " +
                    $"This combination lands in #4596 Phase 1 Session 4. Use unconditional append for now.");
            }

            if (!forceBulkFunction && stream.ActionType == StreamActionType.Start)
            {
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
                if (!forceBulkFunction && stream.ExpectedVersionOnServer.HasValue)
                {
                    // We can supply the version to the events going in
                    stream.PrepareEvents(stream.ExpectedVersionOnServer.Value, eventGraph, sequences, metadataContext);
                    session.QueueOperation(storage.UpdateStreamVersion(stream));
                    foreach (var @event in stream.Events)
                    {
                        session.QueueOperation(storage.QuickAppendEventWithVersion(stream, @event));
                    }

                    // Individual inserts don't use the function, so queue separate tag operations
                    EventTagOperations.QueueTagOperationsByEventId(eventGraph, session, stream);
                }
                else
                {
                    // Tags in TagTables mode are handled inside the PostgreSQL function via
                    // array parameters. In HStore mode the function signature is trimmed
                    // (no per-tag varchar[] params), so tags are written via a follow-up
                    // UPDATE keyed on the event's id after the bulk insert completes.
                    stream.PrepareEvents(0, eventGraph, sequences, metadataContext);
                    var quickAppendEvents = (QuickAppendEventsOperationBase)storage.QuickAppendEvents(stream);
                    quickAppendEvents.Events = eventGraph;
                    session.QueueOperation(quickAppendEvents);

                    if (eventGraph.DcbStorageMode == DcbStorageMode.HStore)
                    {
                        EventTagOperations.QueueTagOperationsByEventId(eventGraph, session, stream);
                    }
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
