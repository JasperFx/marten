using System.Diagnostics.CodeAnalysis;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

internal class InlineFetchPlanner : IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IEventIdentityStrategy<TId> identity,
        StoreOptions options,
        [NotNullWhen(true)] out IAggregateFetchPlan<TDoc, TId>? plan) where TDoc : class where TId : notnull
    {
        // #5144: a null identity strategy means planning came down the natural-key branch. These
        // lifecycle planners match on the projection alone, so without this they would happily hand
        // back a plan holding that null and fail later with a bare NullReferenceException.
        if (identity is null)
        {
            plan = null;
            return false;
        }

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
