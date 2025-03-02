using JasperFx.Events.Grouping;

namespace Marten.Events.Aggregation;

public interface IAggregateGrouper<T> : IJasperFxAggregateGrouper<T, IQuerySession>
{

}
