using System.Diagnostics.CodeAnalysis;
using JasperFx.Events.Projections;
using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

internal class InlineFetchPlanner : IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IDocumentStorage<TDoc, TId> storage, IEventIdentityStrategy<TId> identity, StoreOptions options,
        [NotNullWhen(true)]out IAggregateFetchPlan<TDoc, TId>? plan) where TDoc : class where TId : notnull
    {
        if (options.Projections.TryFindAggregate(typeof(TDoc), out var projection))
        {
            if (projection.Lifecycle == ProjectionLifecycle.Inline)
            {
                plan = new FetchInlinedPlan<TDoc, TId>(options.EventGraph, identity);
                return true;
            }
        }

        plan = null;
        return false;
    }
}
