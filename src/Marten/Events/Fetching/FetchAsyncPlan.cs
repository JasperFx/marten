using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;
using Weasel.Postgresql;
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
    private readonly string _versionSelectionSql;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();
    private readonly string _cachedVersionSelectionSql;
    private readonly IAggregateWriteCache? _cache;
    private string? _initialSql;

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

        if (_events.TenancyStyle == TenancyStyle.Single || _events.GlobalAggregates.Contains(typeof(TDoc)))
        {
            _versionSelectionSql =
                $" left outer join {storage.TableName.QualifiedName} as a on d.stream_id = a.id where (a.mt_version is NULL or d.version > a.mt_version) and d.stream_id = ";
        }
        else
        {
            _versionSelectionSql =
                $" left outer join {storage.TableName.QualifiedName} as a on d.stream_id = a.id and d.tenant_id = a.tenant_id where (a.mt_version is NULL or d.version > a.mt_version) and d.stream_id = ";
        }

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
    ///     Brackets a fetch's reads so they all see ONE snapshot of the database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An async-plan fetch reads the stream version, the snapshot document and the events after the
    ///         snapshot as separate statements. Under READ COMMITTED each of those takes its own snapshot, and
    ///         the async daemon writing the snapshot document in between is enough to make them disagree: the
    ///         delta query re-reads <c>a.mt_version</c> and therefore excludes the very events the snapshot
    ///         read never saw. The aggregate then comes back null for an existing stream, or silently stale.
    ///     </para>
    ///     <para>
    ///         Defined once and used by every path that reads this way, because the two halves have to agree
    ///         and because <c>begin transaction isolation level repeatable read read only</c> is only legal as
    ///         the FIRST statement of a transaction -- a constraint that is invisible at the call site and
    ///         surfaces as a bare <c>25001</c> when it is broken (#5535).
    ///     </para>
    /// </remarks>
    private static void beginSharedSnapshot(ICommandBuilder builder)
    {
        builder.Append("begin transaction isolation level repeatable read read only");
        builder.StartNewCommand();
    }

    /// <summary>
    ///     Closes the bracket opened by <see cref="beginSharedSnapshot" />. Note this <c>end</c> commits the
    ///     transaction it is in, which is why a fetch that emits it cannot share a batch with one holding a
    ///     row lock for a later SaveChanges.
    /// </summary>
    private static void endSharedSnapshot(ICommandBuilder builder)
    {
        builder.StartNewCommand();
        builder.Append("end");
    }

    private void writeEventFetchStatement(TId id,
        ICommandBuilder builder)
    {
        builder.Append(_initialSql!);
        builder.Append(_versionSelectionSql);
        builder.AppendParameter(id);

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
