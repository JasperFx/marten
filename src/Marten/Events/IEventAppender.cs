using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services.BatchQuerying;

namespace Marten.Events;

public interface IEventAppendingStep
{
    ValueTask ApplyAsync(DocumentSessionBase session, EventGraph eventGraph, Queue<long> sequences,
        IEventStorage storage,
        CancellationToken cancellationToken);
}

public interface IEventAppendPreProcessor
{
    IEventAppendingStep TryPreFetch(IBatchedQuery query, DocumentSessionBase session, StreamAction[] actions);
}

internal interface IEventAppender
{
    void ProcessEvents(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjectionsValue);

    Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjections, CancellationToken token);
}
