using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;
using Weasel.Postgresql;
using Weasel.Storage;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Events.Fetching;

namespace Marten.Events.Fetching;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal partial class FetchAsyncPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;
    private readonly IDocumentStorage<TDoc, TId> _storage;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();
    private readonly string _cachedVersionSelectionSql;
    private readonly IAggregateWriteCache? _cache;
    private string? _initialSql;
    private string? _selectedEventFields;
    private int _eventFieldCount;

    public FetchAsyncPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IDocumentStorage<TDoc, TId> storage)
    {
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        _events = events;
        _identityStrategy = identityStrategy;
        _storage = storage;
        var raw = _events.Options.Projections.AggregatorFor<TDoc>();

        // Blame strong typed identifiers for this abomination folks
        _aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                      ?? typeof(IdentityForwardingAggregator<,,,>)
                          .CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, _storage, typeof(TDoc),
                              _storage.IdType, typeof(TId), typeof(IQuerySession));

        // The cached path knows its baseline version up front, so it needs no join to the aggregate
        // table at all -- just the events after the snapshot we already hold.
        _cachedVersionSelectionSql = " where d.stream_id = ";

        // Resolved once per plan, never per fetch. ResolveCache(Type) hands back
        // NulloAggregateWriteCache for a type nobody enrolled, so a store *may* drop this branch and
        // let every take miss -- Marten deliberately does not. The cached path builds an
        // AggregateCacheKey per fetch, which boxes the stream id, and dropping the branch would put
        // that allocation on every FetchForWriting in every store that never opted in.
        if (_events.AggregateWriteCaching.IsEnabled(typeof(TDoc)))
        {
            _cache = _events.AggregateWriteCaching.ResolveCache(typeof(TDoc));
        }
    }

    public bool IsGlobal { get; }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Async;

    /// <summary>
    ///     The events after the snapshot, and the snapshot version they start after (the floor):
    ///     <code>
    ///     with floor as (select coalesce((select a.mt_version from [snapshot table] as a where a.id = :id), 0) as version)
    ///     select [event columns], floor.version
    ///     from floor left outer join mt_events as d on d.stream_id = :id and d.version > floor.version
    ///     order by d.seq_id
    ///     </code>
    ///     With no events, the left join still returns one row, so the floor always comes back.
    /// </summary>
    /// <remarks>
    ///     Every Async fetch runs this BEFORE it loads the snapshot document. Each statement sees the database as
    ///     it is when that statement starts, and the async daemon only moves a snapshot forward, so the document
    ///     loaded afterwards is at or past the floor: only the events newer than it are applied. A document behind
    ///     the floor was deleted or rebuilt in between, which <see cref="eventsAfterSnapshot" /> detects.
    /// </remarks>
    private void writeEventFetchStatement(TId id,
        ICommandBuilder builder)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined && !IsGlobal;

        builder.Append("with floor as (select coalesce((select a.mt_version from ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as a where a.id = ");
        builder.AppendParameter(id);
        if (conjoined)
        {
            builder.Append(" and a.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        builder.Append("), 0) as version) select ");
        builder.Append(_selectedEventFields!);
        builder.Append(", floor.version from floor left outer join ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_events as d on d.stream_id = ");
        builder.AppendParameter(id);
        builder.Append(" and d.version > floor.version");

        // You must do this for performance even if the stream ids were
        // magically unique across tenants
        if (conjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        builder.Append(" order by d.seq_id");
    }

    /// <summary>
    ///     What <see cref="writeEventFetchStatement" /> read: the events after the cut-off, and the cut-off.
    /// </summary>
    private readonly record struct Delta(IReadOnlyList<IEvent> Events, long Floor);

    private async Task<Delta> readDeltaAsync(DbDataReader reader, IEventStorage selector, CancellationToken token)
    {
        var events = new List<IEvent>();
        long floor = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            floor = await reader.GetFieldValueAsync<long>(_eventFieldCount, token).ConfigureAwait(false);

            // An empty delta still returns its floor, on one row with no event in it
            if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false)) continue;

            events.Add(await ((ISelector<IEvent>)selector).ResolveAsync(reader, token).ConfigureAwait(false));
        }

        return new Delta(events, floor);
    }

    /// <summary>
    ///     The events still to apply to the snapshot document, which was loaded after the delta: the ones newer
    ///     than the document.
    /// </summary>
    /// <remarks>
    ///     A document behind the floor, or missing when the floor says there was one, went backwards between the
    ///     two reads, and the events in between were read by neither. With <paramref name="detect" /> on that
    ///     throws <see cref="SnapshotRaceException" /> so the caller can redo the fetch; with it off, the delta is
    ///     applied as it stands.
    /// </remarks>
    private static IReadOnlyList<IEvent> eventsAfterSnapshot(TId id, bool hasSnapshot, long? snapshotVersion,
        Delta delta, bool detect)
    {
        if (detect && delta.Floor > 0 && (!hasSnapshot || snapshotVersion < delta.Floor))
        {
            throw new SnapshotRaceException(typeof(TDoc), id);
        }

        if (!hasSnapshot || snapshotVersion is not { } version || version <= delta.Floor)
        {
            return delta.Events;
        }

        return delta.Events.Where(e => e.Version > version).ToList();
    }

    /// <summary>
    ///     The stream version the fetched aggregate reflects. The stream version is read first, so the
    ///     snapshot or the events can be past it, and the aggregate is then at the later version.
    /// </summary>
    private static long reachedVersion(long streamVersion, bool hasSnapshot, long? snapshotVersion,
        IReadOnlyList<IEvent> events)
    {
        var reached = streamVersion;
        if (hasSnapshot && snapshotVersion > reached) reached = snapshotVersion.Value;
        if (events.Count > 0 && events[^1].Version > reached) reached = events[^1].Version;
        return reached;
    }

    private static long? snapshotVersionFor(DocumentSessionBase session, TId id) =>
        ((IMartenSession)session).Versions.RevisionFor<TDoc, TId>(id);

    /// <summary>
    ///     The delta query for a cache hit: only the events after the version of the snapshot we already
    ///     hold in memory. Mirrors the tenancy branching of <see cref="writeEventFetchStatement" />.
    /// </summary>
    private void writeCachedEventFetchStatement(TId id, long baselineVersion, ICommandBuilder builder)
    {
        builder.Append(_initialSql!);
        builder.Append(_cachedVersionSelectionSql);
        builder.AppendParameter(id);

        builder.Append(" and d.version > ");
        builder.AppendParameter(baselineVersion);

        // You must do this for performance even if the stream ids were
        // magically unique across tenants
        if (_events.TenancyStyle == TenancyStyle.Conjoined && !_events.GlobalAggregates.Contains(typeof(TDoc)))
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        builder.Append(" order by d.seq_id");
    }

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
