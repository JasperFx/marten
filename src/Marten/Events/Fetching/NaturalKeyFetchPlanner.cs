using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

/// <summary>
/// Fetch planner that activates when:
/// 1. The aggregate type has a NaturalKeyDefinition
/// 2. The TId being fetched matches the natural key's OuterType (not Guid/string stream id)
///
/// This planner is registered BEFORE the built-in planners so it gets first crack at matching.
/// </summary>
internal class NaturalKeyFetchPlanner: IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IEventIdentityStrategy<TId> identity,
        StoreOptions options,
        [NotNullWhen(true)] out IAggregateFetchPlan<TDoc, TId>? plan) where TDoc : class where TId : notnull
    {
        if (options.Projections.TryFindAggregate(typeof(TDoc), out var projection))
        {
            var naturalKey = projection.NaturalKeyDefinition;
            if (naturalKey != null && naturalKey.OuterType == typeof(TId))
            {
                // Only match if TId is NOT already a stream identity type (Guid/string)
                // Those are handled by the existing planners
                if (typeof(TId) != typeof(Guid) && typeof(TId) != typeof(string))
                {
                    plan = new FetchNaturalKeyPlan<TDoc, TId>(
                        options.EventGraph,
                        naturalKey,
                        projection.Lifecycle,
                        options);
                    return true;
                }
            }
        }

        plan = null;
        return false;
    }
}
