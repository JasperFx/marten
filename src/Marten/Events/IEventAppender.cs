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

/// <summary>
/// Place holder for just running the inline projection
/// </summary>
public class ExecuteInlineProjectionStep: IEventAppendingStep
{
    private readonly IProjection _projection;
    private readonly IReadOnlyList<StreamAction> _actions;

    public ExecuteInlineProjectionStep(IProjection projection, IReadOnlyList<StreamAction> actions)
    {
        _projection = projection;
        _actions = actions;
    }

    public ValueTask ApplyAsync(DocumentSessionBase session, EventGraph eventGraph, Queue<long> sequences, IEventStorage storage,
        CancellationToken cancellationToken)
    {
        return new ValueTask(_projection.ApplyAsync(session, _actions, cancellationToken));
    }
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
