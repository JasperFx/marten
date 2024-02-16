using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

internal class LiveFetchPlanner: IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IDocumentStorage<TDoc, TId> storage, IEventIdentityStrategy<TId> identity,
        StoreOptions options, out IAggregateFetchPlan<TDoc, TId> plan) where TDoc : class
    {
        plan = new FetchLivePlan<TDoc, TId>(options.EventGraph, identity, storage);
        return true;
    }
}