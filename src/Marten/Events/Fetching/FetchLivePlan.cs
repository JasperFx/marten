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

namespace Marten.Events.Fetching;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal partial class FetchLivePlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly IIdentitySetter<TDoc, TId> _documentStorage;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;
    private readonly OpenTelemetryOptions _telemetry;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();
    private readonly IAggregateWriteCache? _cache;

    public FetchLivePlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IIdentitySetter<TDoc, TId> documentStorage)
    {
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        _identityStrategy = identityStrategy;
        _documentStorage = documentStorage;
        _telemetry = events.Options.OpenTelemetry;

        // Resolved once per plan, never per fetch, and left null when nobody enrolled this type -- the
        // cached path builds an AggregateCacheKey per fetch, which boxes the stream id, and a null check
        // keeps that allocation off every FetchForWriting in every store that never opted in.
        if (events.AggregateWriteCaching.IsEnabled(typeof(TDoc)))
        {
            _cache = events.AggregateWriteCaching.ResolveCache(typeof(TDoc));
        }

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
    }

    public bool IsGlobal { get; }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Live;

    /// <summary>
    ///     Composes the cache key for a stream. Getting the tenant or database wrong here would be a
    ///     cross-tenant data leak rather than a performance bug, so both are always part of the key.
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
