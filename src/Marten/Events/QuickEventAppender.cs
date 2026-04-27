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

        // The quick-append path never reads from this queue (PrepareEvents calls
        // applyQuickMetadata, not applyRichMetadata, and only the rich variant
        // dequeues from it). Hoist a single throwaway instance out of the
        // per-stream loop so we don't allocate one per stream on every save.
        var sequences = new Queue<long>();

        foreach (var stream in session.WorkTracker.Streams.Where(x => x.Events.Any()))
        {
            stream.TenantId ??= session.TenantId;
            if (stream.ActionType == StreamActionType.Start)
            {
                stream.PrepareEvents(0, eventGraph, sequences, session);
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
                if (stream.ExpectedVersionOnServer.HasValue)
                {
                    // We can supply the version to the events going in
                    stream.PrepareEvents(stream.ExpectedVersionOnServer.Value, eventGraph, sequences, session);
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
                    // Tags are handled inside the PostgreSQL function via array parameters
                    stream.PrepareEvents(0, eventGraph, sequences, session);
                    var quickAppendEvents = (QuickAppendEventsOperationBase)storage.QuickAppendEvents(stream);
                    quickAppendEvents.Events = eventGraph;
                    session.QueueOperation(quickAppendEvents);
                }
            }
        }
    }

    public async Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session, IInlineProjection<IDocumentOperations>[] inlineProjections,
        CancellationToken token)
    {
        registerOperationsForStreams(eventGraph, session);

        foreach (var projection in inlineProjections)
        {
            await projection.ApplyAsync(session, session.WorkTracker.Streams.ToList(), token).ConfigureAwait(false);
        }
    }
}
