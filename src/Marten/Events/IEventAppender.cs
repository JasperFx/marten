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

/// <summary>
/// Just a place holder for "I do not apply, do nothing"
/// </summary>
public class NulloEventAppendStep: IEventAppendingStep
{
    public ValueTask ApplyAsync(DocumentSessionBase session, EventGraph eventGraph, Queue<long> sequences, IEventStorage storage,
        CancellationToken cancellationToken)
    {
        return new ValueTask();
    }
}


internal interface IEventAppender
{
    Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjections, CancellationToken token);
}
