using System;
using JasperFx.Events.Projections;

namespace Marten.Events.Projections;

[Obsolete("Consolidate in a single IProjectionSource? See experimental branch")]
internal interface ILiveAggregatorSource<T>
{
    IAggregator<T, IQuerySession> BuildAggregator(StoreOptions options);
}
