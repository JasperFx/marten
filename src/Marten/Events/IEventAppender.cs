using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Descriptors;
using JasperFx.Events.Projections;
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
        IInlineProjection<IDocumentOperations>[] inlineProjections, CancellationToken token);
}

internal class GlobalEventAppenderDecorator: IEventAppender
{
    private readonly IEventAppender _inner;

    public GlobalEventAppenderDecorator(IEventAppender inner)
    {
        _inner = inner;
    }

    public List<Type> AggregateTypes { get; } = new();
    public List<Type> EventTypes { get; } = new();

    public bool Matches(StreamAction action)
    {
        if (!action.Events.Any()) return false;
        if (action.AggregateType != null && AggregateTypes.Contains(action.AggregateType)) return true;

        if (action.Events.Select(x => x.EventType).Any(x => EventTypes.Contains(x))) return true;

        return false;
    }

    public Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session, IInlineProjection<IDocumentOperations>[] inlineProjections,
        CancellationToken token)
    {
        var streamActions = session.WorkTracker.Streams.Where(Matches).ToArray();
        foreach (var action in streamActions)
        {
            // Route the stream (and its mt_events rows) to the default tenant so
            // that AddGlobalProjection's documented "single-tenanted within a
            // conjoined tenancy" guarantee still holds.
            //
            // NOTE: StreamAction.TenantId's setter propagates the new value onto
            // every IEvent in the stream (see JasperFx.Events StreamAction). If
            // we stopped there, inline SingleStreamProjection Create(IEvent<T>)
            // / Apply(IEvent<T>, TDoc) convention methods would read
            // @event.TenantId as "*DEFAULT*" instead of the session tenant
            // under which the user appended them — see
            // https://github.com/JasperFx/marten/issues/4270. To preserve the
            // session tenant on in-flight events, we capture the original tenant
            // before forcing the default on the action, then restore it on each
            // event. The stream-level action.TenantId keeps the default tenant,
            // so storage (mt_events / mt_streams) stays single-tenanted.
            var originalTenantId = action.TenantId;
            action.TenantId = StorageConstants.DefaultTenantId;
            if (originalTenantId != null && originalTenantId != StorageConstants.DefaultTenantId)
            {
                foreach (var e in action.Events)
                {
                    e.TenantId = originalTenantId;
                }
            }
        }

        return _inner.ProcessEventsAsync(eventGraph, session, inlineProjections, token);
    }

    public void ReadEventTypes(EventGraph graph)
    {
        AggregateTypes.AddRange(graph.GlobalAggregates);
        var eventTypes = AggregateTypes.SelectMany(aggregateType =>
        {
            return graph
                .Options
                .Projections
                .All
                .Where(x => x.Type == SubscriptionType.SingleStreamProjection &&
                            x.PublishedTypes().Contains(aggregateType))
                .OfType<ProjectionBase>()
                .SelectMany(x => x.IncludedEventTypes);
        }).Distinct();

        EventTypes.AddRange(eventTypes);
    }
}
