using System.Collections.Generic;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections;

internal interface IGrouper<TId>
{
    void Apply(IEnumerable<IEvent> events, ITenantSliceGroup<TId> grouping);
}
