using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

public interface IFetchPlanner
{
    bool TryMatch<TDoc, TId>(IDocumentStorage<TDoc, TId> storage, IEventIdentityStrategy<TId> identity,
        StoreOptions options, out IAggregateFetchPlan<TDoc, TId> plan) where TDoc : class;
}
