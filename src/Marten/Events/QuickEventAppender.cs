using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Events.Projections;
using Marten.Internal.Sessions;

namespace Marten.Events;

internal class QuickEventAppender: IEventAppender
{
    private static void registerOperationsForStreams(EventGraph eventGraph, DocumentSessionBase session)
    {
        var storage = session.EventStorage();

        foreach (var stream in session.WorkTracker.Streams.Where(x => x.Events.Any()))
        {
            stream.TenantId ??= session.TenantId;

            // Not really using it, just need a stand in
            var sequences = new Queue<long>();
            if (stream.ActionType == StreamActionType.Start)
            {
                stream.PrepareEvents(0, eventGraph, sequences, session);
                session.QueueOperation(storage.InsertStream(stream));

                foreach (var @event in stream.Events)
                {
                    session.QueueOperation(storage.QuickAppendEventWithVersion(eventGraph, session, stream, @event));
                }
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
                        session.QueueOperation(storage.QuickAppendEventWithVersion(eventGraph, session, stream, @event));
                    }
                }
                else
                {
                    stream.PrepareEvents(0, eventGraph, sequences, session);
                    var quickAppendEvents = (QuickAppendEventsOperationBase)storage.QuickAppendEvents(stream);
                    quickAppendEvents.Events = eventGraph;
                    session.QueueOperation(quickAppendEvents);
                }
            }
        }
    }

    public async Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session, IProjection[] inlineProjections,
        CancellationToken token)
    {
        registerOperationsForStreams(eventGraph, session);

        foreach (var projection in inlineProjections)
        {
            await projection.ApplyAsync(session, session.WorkTracker.Streams.ToList(), token).ConfigureAwait(false);
        }
    }
}
