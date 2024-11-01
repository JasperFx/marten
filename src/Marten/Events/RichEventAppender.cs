using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Services.BatchQuerying;

namespace Marten.Events;

internal class RichEventAppender: IEventAppender
{
    public async Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjections, CancellationToken token)
    {
        var batch = session.CreateBatchQuery();

        var fetcher = new EventSequenceFetcher(eventGraph, session.WorkTracker.Streams.Sum(x => x.Events.Count));

        var sequencesFetch = batch.AddItem(fetcher);

        var storage = session.EventStorage();
        var streamActions = session.WorkTracker.Streams.Where(x => x.Events.Any()).ToArray();

        var appends = streamActions
            .Where(x => x.ActionType == StreamActionType.Append)
            .Select(x => new RichStreamAppendingStep(x, batch, storage))
            .ToArray();

        await batch.Execute(token).ConfigureAwait(false);

        var sequences = await sequencesFetch.ConfigureAwait(false);

        foreach (var append in appends)
        {
            await append.ApplyAsync(session, eventGraph, sequences, storage, token).ConfigureAwait(false);
        }

        foreach (var stream in streamActions)
        {
            stream.TenantId ??= session.TenantId;

            if (stream.ActionType == StreamActionType.Start)
            {
                stream.PrepareEvents(0, eventGraph, sequences, session);
                session.QueueOperation(storage.InsertStream(stream));
            }
            else if (eventGraph.UseMandatoryStreamTypeDeclaration && stream.IsStarting())
            {
                throw new NonExistentStreamException(eventGraph.StreamIdentity == StreamIdentity.AsGuid
                    ? stream.Id
                    : stream.Key);
            }

            foreach (var @event in stream.Events)
            {
                session.QueueOperation(storage.AppendEvent(eventGraph, session, stream, @event));
            }
        }

        // TODO -- look for opportunities to batch up the requests here too!
        foreach (var projection in inlineProjections)
        {
            await projection.ApplyAsync(session, session.WorkTracker.Streams.ToList(), token).ConfigureAwait(false);
        }
    }
}

internal class RichStreamAppendingStep: IEventAppendingStep
{
    private readonly StreamAction _stream;
    private readonly Task<StreamState> _fetcher;

    public RichStreamAppendingStep(StreamAction stream, IBatchedQuery query, IEventStorage storage)
    {
        _stream = stream;
        _fetcher = query.AddItem(storage.QueryForStream(stream));
    }

    public async ValueTask ApplyAsync(DocumentSessionBase session, EventGraph eventGraph,
        Queue<long> sequences,
        IEventStorage storage,
        CancellationToken cancellationToken)
    {
        var state = await _fetcher.ConfigureAwait(false);

        if (state == null)
        {
            _stream.PrepareEvents(0, eventGraph, sequences, session);
            session.QueueOperation(storage.InsertStream(_stream));
        }
        else
        {
            if (state.IsArchived)
            {
                throw new InvalidStreamOperationException(
                    $"Attempted to append event to archived stream with Id '{state.Id}'.");
            }

            _stream.PrepareEvents(state.Version, eventGraph, sequences, session);
            session.QueueOperation(storage.UpdateStreamVersion(_stream));
        }
    }
}
