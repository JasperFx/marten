using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Internal.Storage;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly IDocumentStorage<TDoc, TId> _documentStorage;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;

    public FetchLivePlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IDocumentStorage<TDoc, TId> documentStorage)
    {
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        _identityStrategy = identityStrategy;
        _documentStorage = documentStorage;

        var raw = events.Options.Projections.AggregatorFor<TDoc>();

        _aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                      ?? typeof(IdentityForwardingAggregator<,,,>).CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, _documentStorage, typeof(TDoc), _documentStorage.IdType, typeof(TId), typeof(IQuerySession));
    }

    public bool IsGlobal { get; }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Live;
}
