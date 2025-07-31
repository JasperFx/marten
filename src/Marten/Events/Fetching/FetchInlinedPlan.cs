using JasperFx.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

internal partial class FetchInlinedPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;

    internal FetchInlinedPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy)
    {
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        _events = events;
        _identityStrategy = identityStrategy;
    }

    public bool IsGlobal { get; }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Inline;

    private static IDocumentStorage<TDoc, TId> findDocumentStorage(QuerySession session)
    {
        IDocumentStorage<TDoc, TId>? storage = null;
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            storage = session.Options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
            // Opt into the identity map mechanics for this aggregate type just in case
            // you're using a lightweight session
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = session.StorageFor<TDoc, TId>();
        }

        return storage;
    }

}
