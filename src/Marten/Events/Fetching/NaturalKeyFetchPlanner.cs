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

            // #5344: this used to exclude Guid and string outright, on the reasoning that both are
            // stream identity types handled by the built-in planners. Only one of them is, though —
            // whichever the store actually uses. On a Guid-identity store a primitive `string`
            // natural key was refused here and then died on EnsureAsStringStorage's "configured to
            // identify streams with Guids". EventStore.IsNaturalKeyIdentity narrows the exclusion to
            // the store's own stream identity type, which keeps the ambiguous case (a string key on
            // a string-identity store) resolving to the stream as it always has.
            if (naturalKey != null && naturalKey.OuterType == typeof(TId) &&
                EventStore.IsNaturalKeyIdentity<TDoc, TId>(options))
            {
                plan = new FetchNaturalKeyPlan<TDoc, TId>(
                    options.EventGraph,
                    naturalKey,
                    projection.Lifecycle,
                    options);
                return true;
            }
        }

        plan = null;
        return false;
    }
}
