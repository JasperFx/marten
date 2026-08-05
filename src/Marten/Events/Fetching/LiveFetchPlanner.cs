using System.Diagnostics.CodeAnalysis;
using JasperFx.Events.Aggregation;
using Marten.Exceptions;
using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

internal class LiveFetchPlanner: IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IEventIdentityStrategy<TId> identity,
        StoreOptions options, [NotNullWhen(true)] out IAggregateFetchPlan<TDoc, TId>? plan) where TDoc : class where TId : notnull
    {
        // #5144: a null identity strategy means planning came down the natural-key branch. These
        // lifecycle planners match on the projection alone, so without this they would happily hand
        // back a plan holding that null and fail later with a bare NullReferenceException.
        if (identity is null)
        {
            plan = null;
            return false;
        }

        IIdentitySetter<TDoc, TId> identitySetter = new NulloIdentitySetter<TDoc, TId>();

        // Yeah, this is smelly, but at least it would only happen *once* at runtime
        try
        {
            // Try to overwrite w/ a real document storage
            identitySetter = options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
        }
        catch (InvalidDocumentException)
        {
            // there's no identity, just use the nullo strategy
        }

        plan = new FetchLivePlan<TDoc, TId>(options.EventGraph, identity, identitySetter);
        return true;
    }
}
