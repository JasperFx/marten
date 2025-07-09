using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Internal.Storage;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;
    private readonly IDocumentStorage<TDoc, TId> _storage;
    private readonly string _versionSelectionSql;
    private string? _initialSql;

    public FetchAsyncPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IDocumentStorage<TDoc, TId> storage)
    {
        _events = events;
        _identityStrategy = identityStrategy;
        _storage = storage;
        var raw = _events.Options.Projections.AggregatorFor<TDoc>();

        // Blame strong typed identifiers for this abomination folks
        _aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                      ?? typeof(IdentityForwardingAggregator<,,,>)
                          .CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, _storage, typeof(TDoc),
                              _storage.IdType, typeof(TId), typeof(IQuerySession));

        if (_events.TenancyStyle == TenancyStyle.Single)
        {
            _versionSelectionSql =
                $" left outer join {storage.TableName.QualifiedName} as a on d.stream_id = a.id where (a.mt_version is NULL or d.version > a.mt_version) and d.stream_id = ";
        }
        else
        {
            _versionSelectionSql =
                $" left outer join {storage.TableName.QualifiedName} as a on d.stream_id = a.id and d.tenant_id = a.tenant_id where (a.mt_version is NULL or d.version > a.mt_version) and d.stream_id = ";
        }
    }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Async;

    private void writeEventFetchStatement(TId id,
        ICommandBuilder builder)
    {
        builder.Append(_initialSql!);
        builder.Append(_versionSelectionSql);
        builder.AppendParameter(id);

        // You must do this for performance even if the stream ids were
        // magically unique across tenants
        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }
    }
}
