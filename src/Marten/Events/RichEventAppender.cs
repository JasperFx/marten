using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;

namespace Marten.Events;

internal class RichEventAppender: IEventAppender
{
    public void ProcessEvents(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjections)
    {
        var storage = session.EventStorage();

        var fetcher = new EventSequenceFetcher(eventGraph, session.WorkTracker.Streams.Sum(x => x.Events.Count));
        var sequences = session.ExecuteHandler(fetcher);


        foreach (var stream in session.WorkTracker.Streams.Where(x => x.Events.Any()))
        {
            stream.TenantId ??= session.TenantId;

            if (stream.ActionType == StreamActionType.Start)
            {
                stream.PrepareEvents(0, eventGraph, sequences, session);
                session.QueueOperation(storage.InsertStream(stream));
            }
            else
            {
                var handler = storage.QueryForStream(stream);
                var state = session.ExecuteHandler(handler);

                if (state == null)
                {
                    stream.PrepareEvents(0, eventGraph, sequences, session);
                    session.QueueOperation(storage.InsertStream(stream));
                }
                else
                {
                    if (state.IsArchived)
                    {
                        throw new InvalidStreamOperationException(
                            $"Attempted to append event to archived stream with Id '{state.Id}'.");
                    }

                    stream.PrepareEvents(state.Version, eventGraph, sequences, session);
                    session.QueueOperation(storage.UpdateStreamVersion(stream));
                }
            }

            foreach (var @event in stream.Events)
            {
                session.QueueOperation(storage.AppendEvent(eventGraph, session, stream, @event));
            }
        }

        foreach (var projection in inlineProjections)
        {
            projection.Apply(session, session.WorkTracker.Streams.ToList());
        }
    }

    public async Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjections, CancellationToken token)
    {
        var fetcher = new EventSequenceFetcher(eventGraph, session.WorkTracker.Streams.Sum(x => x.Events.Count));
        var sequences = await session.ExecuteHandlerAsync(fetcher, token).ConfigureAwait(false);


        var storage = session.EventStorage();

        foreach (var stream in session.WorkTracker.Streams.Where(x => x.Events.Any()))
        {
            stream.TenantId ??= session.TenantId;

            if (stream.ActionType == StreamActionType.Start)
            {
                stream.PrepareEvents(0, eventGraph, sequences, session);
                session.QueueOperation(storage.InsertStream(stream));
            }
            else
            {
                var handler = storage.QueryForStream(stream);
                var state = await session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);

                if (state == null)
                {
                    stream.PrepareEvents(0, eventGraph, sequences, session);
                    session.QueueOperation(storage.InsertStream(stream));
                }
                else
                {
                    if (state.IsArchived)
                    {
                        throw new InvalidStreamOperationException(
                            $"Attempted to append event to archived stream with Id '{state.Id}'.");
                    }

                    stream.PrepareEvents(state.Version, eventGraph, sequences, session);
                    session.QueueOperation(storage.UpdateStreamVersion(stream));
                }
            }

            foreach (var @event in stream.Events)
            {
                session.QueueOperation(storage.AppendEvent(eventGraph, session, stream, @event));
            }
        }

        foreach (var projection in inlineProjections)
        {
            await projection.ApplyAsync(session, session.WorkTracker.Streams.ToList(), token).ConfigureAwait(false);
        }
    }
}
