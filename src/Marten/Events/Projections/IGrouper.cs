using System.Collections.Generic;
using Baseline;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections
{
    internal interface IGrouper<TId>
    {
        void Apply(IEnumerable<IEvent> events, ITenantSliceGroup<TId> grouping);
    }
}
