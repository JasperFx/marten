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

    internal static IDocumentStorage<TDoc, TId> findDocumentStorage(QuerySession session)
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
            // #6148: as fetchForWriting, so an aggregate with a strong-typed id can be read by its stream's Guid or
            // string. session.StorageFor<TDoc, TId>() refuses an id type that is not the document's own.
            storage = ((IMartenSession)session).Options.ResolveCorrectedDocumentStorage<TDoc, TId>(session.TrackingMode);
        }

        return storage;
    }

    /// <summary>
    ///     Take the instance pending events were just folded onto out of the identity map, so the inline projection
    ///     does not fold them onto it a second time at commit.
    /// </summary>
    /// <remarks>
    ///     #6148: the map is keyed by the aggregate's own id type. For a strong-typed id that is the wrapper, not the
    ///     stream's Guid or string, so ejecting by <paramref name="id" /> alone removes nothing. The storage ejects by
    ///     the document's own identity.
    /// </remarks>
    internal static void EjectFoldedInstance(DocumentSessionBase session, IDocumentStorage<TDoc, TId> storage, TId id,
        TDoc? stored)
    {
        session.EjectAggregateFromIdentityMap<TDoc, TId>(id);

        if (stored != null)
        {
            storage.Eject(session, stored);
        }
    }
}
