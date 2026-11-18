using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Events.Fetching;
using Marten.Storage;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly EventGraph _events;
    private readonly IAggregateWriteCache? _cache;
    private readonly IIdentitySetter<TDoc, TId> _documentStorage;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;
    private readonly OpenTelemetryOptions _telemetry;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers supply source-generator-backed serializer impls per the AOT publishing guide.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
    public FetchLivePlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IIdentitySetter<TDoc, TId> documentStorage)
    {
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        _events = events;
        _identityStrategy = identityStrategy;
        _documentStorage = documentStorage;
        _telemetry = events.Options.OpenTelemetry;

        var raw = events.Options.Projections.AggregatorFor<TDoc>();

        // yeah, I know, this is kind of gross
        if (documentStorage is NulloIdentitySetter<TDoc, TId>)
        {
            _aggregator = (IAggregator<TDoc, TId, IQuerySession>?)raw;
        }
        else
        {
            var simpleType = events.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
            var idType = documentStorage is IDocumentStorage<TDoc, TId> s ? s.IdType : typeof(TId);

            // The goofy identity forwarding thing is to deal with custom value types. Of course.
            _aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                          ?? typeof(IdentityForwardingAggregator<,,,>).CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, _documentStorage, typeof(TDoc), idType, simpleType, typeof(IQuerySession));
        }

        // #5225: CacheAggregatesForWriting<T>() for a Live aggregate. Resolved once per plan, and only for an
        // enrolled type, so a store that never opted in builds no cache key on any fetch. See FetchAsyncPlan.
        if (events.AggregateWriteCaching.IsEnabled(typeof(TDoc)))
        {
            _cache = events.AggregateWriteCaching.ResolveCache(typeof(TDoc));
        }
    }

    public bool IsGlobal { get; }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Live;

    /// <summary>
    ///     The cache key for a stream. The database and the tenant are always part of it, because getting either
    ///     wrong would hand one tenant's aggregate to another. The same key as <see cref="FetchAsyncPlan{TDoc,TId}" />.
    /// </summary>
    private AggregateCacheKey cacheKeyFor(DocumentSessionBase session, TId id)
    {
        return new AggregateCacheKey(
            typeof(TDoc),
            session.Database.Identifier,
            IsGlobal ? AggregateCacheKey.GlobalTenant : session.TenantId,
            id);
    }
}
