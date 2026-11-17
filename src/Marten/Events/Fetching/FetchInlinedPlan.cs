using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;
using JasperFx.Events.Fetching;
using Marten.Events.Cold;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Fetching;

internal partial class FetchInlinedPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();
    private readonly IAggregateWriteCache? _cache;

    internal FetchInlinedPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy)
    {
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        _events = events;
        _identityStrategy = identityStrategy;

        // See FetchAsyncPlan's constructor for why the enablement branch stays rather than leaning on
        // ResolveCache(Type) returning the nullo cache.
        if (events.AggregateWriteCaching.IsEnabled(typeof(TDoc)))
        {
            _cache = events.AggregateWriteCaching.ResolveCache(typeof(TDoc));
        }
    }

    public bool IsGlobal { get; }

    private AggregateCacheKey cacheKeyFor(DocumentSessionBase session, TId id)
    {
        return new AggregateCacheKey(
            typeof(TDoc),
            session.Database.Identifier,
            IsGlobal ? AggregateCacheKey.GlobalTenant : session.TenantId,
            id);
    }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Inline;

    private static IDocumentStorage<TDoc, TId> findDocumentStorage(QuerySession session)
    {
        IDocumentStorage<TDoc, TId>? storage = null;
        if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            storage = ((IMartenSession)session).Options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
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

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers supply source-generator-backed serializer impls per the AOT publishing guide.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
    private IAggregator<TDoc, TId, IQuerySession> aggregatorFor(IDocumentStorage<TDoc, TId> storage)
    {
        var raw = _events.Options.Projections.AggregatorFor<TDoc>();
        return raw as IAggregator<TDoc, TId, IQuerySession>
               ?? typeof(IdentityForwardingAggregator<,,,>)
                   .CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, storage, typeof(TDoc),
                       storage.IdType, typeof(TId), typeof(IQuerySession));
    }

    private ColdAggregationStart<TDoc, TId>? coldStartFor(IDocumentStorage<TDoc, TId> storage) =>
        ColdAggregationStart.For<TDoc, TId>(_events, () => aggregatorFor(storage));

    /// <summary>
    ///     For a stored document that is missing: the start from the cold part when the application moved the
    ///     document out with the stream's events, as the inline projection itself starts. Null without a cold store.
    /// </summary>
    private async Task<TDoc?> startFromColdAsync(QuerySession session, IDocumentStorage<TDoc, TId> storage, TId id,
        CancellationToken token)
    {
        if (id is not string streamKey || coldStartFor(storage) is not { } cold) return null;

        var start = await cold.StartFetchAsync(session, streamKey, id, storage, false, token).ConfigureAwait(false);
        return start?.Start;
    }
}
