using System;
using System.Collections.Generic;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections;

/// <summary>
///     Assigns an event to only one stream
/// </summary>
/// <typeparam name="TId"></typeparam>
/// <typeparam name="TEvent"></typeparam>
internal class SingleStreamEventGrouper<TId, TEvent>: IGrouper<TId>
{
    private readonly Func<IEvent<TEvent>, TId> _func;

    public SingleStreamEventGrouper(Func<IEvent<TEvent>, TId> expression)
    {
        _func = expression;
    }

    public void Apply(IEnumerable<IEvent> events, ITenantSliceGroup<TId> grouping)
    {
        grouping.AddEventsWithMetadata(_func, events);
    }
}
