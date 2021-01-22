using System.Collections.Generic;
using Baseline;

namespace Marten.Events.Projections
{
    internal interface IGrouper<TId>
    {
        void Group(IEnumerable<IEvent> events, EventGrouping<TId> grouping);
    }
}
