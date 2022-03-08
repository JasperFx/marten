using System.Collections.Generic;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Assigns an event to only one stream based on its stream id
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <typeparam name="TEvent"></typeparam>
    internal class SingleStreamGrouperByEventStreamId<TId, TEvent>: IGrouper<TId> where TEvent : notnull
    {
        public void Apply(IEnumerable<IEvent> events, ITenantSliceGroup<TId> grouping)
        {
            grouping.AddEvents<TEvent>(events);
        }
    }
}
