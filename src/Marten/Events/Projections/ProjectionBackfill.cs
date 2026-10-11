using System;
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Patching;

namespace Marten.Events.Projections;

/// <summary>
///     Fills a member that was added to a projected document without a version bump. The projection keeps
///     running: the backfill folds only the event types declared here into a side table, from the start of
///     the store up to the projection's live position, and hands the member over to the documents that still
///     exist when it has caught up.
/// </summary>
/// <remarks>
///     The patches see one event at a time and nothing of the document, in event order per document, starting
///     from an empty object. The projection's own Apply must produce the same member for every event.
/// </remarks>
public sealed class ProjectionBackfill<TDoc, TId> where TDoc : notnull where TId : notnull
{
    private readonly Dictionary<Type, Action<IPatchExpression<TDoc>, IEvent>> _handlers = new();

    internal ProjectionBackfill(string name)
    {
        Name = name;
    }

    /// <summary>
    ///     Identifies this backfill in <c>mt_projection_backfills</c>. Renaming it runs it again.
    /// </summary>
    public string Name { get; }

    internal IReadOnlyCollection<Type> EventTypes => _handlers.Keys;

    public ProjectionBackfill<TDoc, TId> On<TEvent>(Action<IPatchExpression<TDoc>, TEvent> patch) where TEvent : notnull
    {
        _handlers[typeof(TEvent)] = (expression, e) => patch(expression, (TEvent)e.Data);
        return this;
    }

    /// <summary>
    ///     As <see cref="On{TEvent}" />, for a patch that needs the event's metadata.
    /// </summary>
    public ProjectionBackfill<TDoc, TId> OnEvent<TEvent>(Action<IPatchExpression<TDoc>, IEvent<TEvent>> patch) where TEvent : notnull
    {
        _handlers[typeof(TEvent)] = (expression, e) => patch(expression, (IEvent<TEvent>)e);
        return this;
    }

    internal void Apply(IPatchExpression<TDoc> patch, IEvent e)
    {
        if (_handlers.TryGetValue(e.EventType, out var handler))
        {
            handler(patch, e);
        }
    }
}
