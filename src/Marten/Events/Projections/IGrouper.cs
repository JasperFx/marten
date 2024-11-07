using System.Collections.Generic;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections;

internal interface IGrouper<TId>
{
    void Apply(IEnumerable<IEvent> events, IEventGrouping<TId> grouping);
}
