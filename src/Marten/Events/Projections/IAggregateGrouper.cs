using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Plugin point to create custom event to aggregate grouping that requires database lookup
    /// as part of the sorting of events into aggregate slices
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    public interface IAggregateGrouper<TId>
    {
        /// <summary>
        /// Apply custom grouping rules to apply events to one or many aggregates
        /// </summary>
        /// <param name="session"></param>
        /// <param name="events"></param>
        /// <param name="grouping"></param>
        /// <returns></returns>
        Task Group(IQuerySession session, IEnumerable<IEvent> events, ITenantSliceGroup<TId> grouping);
    }
}
