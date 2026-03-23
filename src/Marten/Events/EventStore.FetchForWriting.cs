#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Events.Fetching;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events;

internal partial class EventStore: IEventIdentityStrategy<Guid>, IEventIdentityStrategy<string>
{
    private ImHashMap<(Type, Type), object> _fetchStrategies = ImHashMap<(Type, Type), object>.Empty;

    async Task<IEventStorage> IEventIdentityStrategy<Guid>.EnsureEventStorageExists<T>(
        DocumentSessionBase session, CancellationToken cancellation)
    {
        var selector = _store.Events.EnsureAsGuidStorage(_session);
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);

        return selector;
    }

    IEventStream<TDoc> IEventIdentityStrategy<Guid>.StartStream<TDoc>(TDoc? document, DocumentSessionBase session,
        Guid id, CancellationToken cancellation) where TDoc : class
    {
        var action = _store.Events.StartEmptyStream(session, id);
        action.AggregateType = typeof(TDoc);
        action.ExpectedVersionOnServer = 0;

        return new EventStream<TDoc>(session, _store.Events, id, document, cancellation, action);
    }

    IEventStream<TDoc> IEventIdentityStrategy<Guid>.AppendToStream<TDoc>(TDoc? document, DocumentSessionBase session,
        Guid id, long version, CancellationToken cancellation) where TDoc : class
    {
        var action = session.Events.Append(id);
        action.ExpectedVersionOnServer = version;
        return new EventStream<TDoc>(session, _store.Events, id, document, cancellation, action);
    }

    IQueryHandler<IReadOnlyList<IEvent>> IEventIdentityStrategy<Guid>.BuildEventQueryHandler(bool isGlobal, Guid id,
        IEventStorage selector, ISqlFragment? filter = null)
    {
        var statement = new EventStatement(selector, _store.Options.EventGraph) { StreamId = id, TenantId = isGlobal ? StorageConstants.DefaultTenantId : _tenant.TenantId};
        if (filter != null)
        {
            statement.Filters = [filter];
        }

        return new ListQueryHandler<IEvent>(statement, selector);
    }

    IQueryHandler<IReadOnlyList<IEvent>> IEventIdentityStrategy<Guid>.BuildEventQueryHandler(bool isGlobal, Guid id,
        ISqlFragment? filter)
    {
        var selector = _store.Events.EnsureAsGuidStorage(_session);
        var statement = new EventStatement(selector, _store.Options.EventGraph) { StreamId = id, TenantId = isGlobal ? StorageConstants.DefaultTenantId : _tenant.TenantId };
        if (filter != null)
        {
            statement.Filters = [filter];
        }

        return new ListQueryHandler<IEvent>(statement, selector);
    }

    IQueryHandler<IReadOnlyList<IEvent>> IEventIdentityStrategy<string>.BuildEventQueryHandler(bool isGlobal, string id,
        ISqlFragment? filter)
    {
        var selector = _store.Events.EnsureAsStringStorage(_session);
        var statement = new EventStatement(selector, _store.Options.EventGraph) { StreamKey = id, TenantId = isGlobal ? StorageConstants.DefaultTenantId : _tenant.TenantId };
        if (filter != null)
        {
            statement.Filters = [filter];
        }

        return new ListQueryHandler<IEvent>(statement, selector);
    }

    async Task<IEventStorage> IEventIdentityStrategy<string>.EnsureEventStorageExists<T>(
        DocumentSessionBase session, CancellationToken cancellation)
    {
        var selector = _store.Events.EnsureAsStringStorage(_session);
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);

        return selector;
    }

    IEventStream<TDoc> IEventIdentityStrategy<string>.StartStream<TDoc>(TDoc? document, DocumentSessionBase session,
        string id, CancellationToken cancellation) where TDoc : class
    {
        var action = _store.Events.StartEmptyStream(session, id);
        action.AggregateType = typeof(TDoc);
        action.ExpectedVersionOnServer = 0;

        return new EventStream<TDoc>(session, _store.Events, id, document, cancellation, action);
    }

    IEventStream<TDoc> IEventIdentityStrategy<string>.AppendToStream<TDoc>(TDoc? document,
        DocumentSessionBase session, string id, long version, CancellationToken cancellation) where TDoc : class
    {
        var action = session.Events.Append(id);
        action.ExpectedVersionOnServer = version;
        return new EventStream<TDoc>(session, _store.Events, id, document, cancellation, action);
    }

    IQueryHandler<IReadOnlyList<IEvent>> IEventIdentityStrategy<string>.BuildEventQueryHandler(bool isGlobal, string id,
        IEventStorage selector, ISqlFragment? filter = null)
    {
        var statement = new EventStatement(selector, _store.Options.EventGraph) { StreamKey = id, TenantId = isGlobal ? StorageConstants.DefaultTenantId : _tenant.TenantId };
        if (filter != null)
        {
            statement.Filters = [filter];
        }

        return new ListQueryHandler<IEvent>(statement, selector);
    }

    public Task<IEventStream<T>> FetchForWriting<T, TId>(TId id, CancellationToken cancellation = default)
        where T : class where TId : notnull
    {
        var plan = FindFetchPlan<T, TId>();
        return plan.FetchForWriting(_session, id, false, cancellation);
    }

    public Task<IEventStream<T>> FetchForExclusiveWriting<T, TId>(TId id, CancellationToken cancellation = default)
        where T : class where TId : notnull
    {
        var plan = FindFetchPlan<T, TId>();
        return plan.FetchForWriting(_session, id, true, cancellation);
    }

    public ValueTask<T?> FetchLatest<T, TId>(TId id, CancellationToken cancellation = default)
        where T : class where TId : notnull
    {
        var plan = FindFetchPlan<T, TId>();
        return plan.FetchForReading(_session, id, cancellation);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(Guid id, CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, Guid>();
        return plan.FetchForWriting(_session, id, false, cancellation);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(string key, CancellationToken cancellation = default)
        where T : class
    {
        var plan = FindFetchPlan<T, string>();
        return plan.FetchForWriting(_session, key, false, cancellation);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(Guid id, long initialVersion,
        CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, Guid>();
        return plan.FetchForWriting(_session, id, initialVersion, cancellation);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(string key, long initialVersion,
        CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, string>();
        return plan.FetchForWriting(_session, key, initialVersion, cancellation);
    }

    public Task<IEventStream<T>> FetchForExclusiveWriting<T>(Guid id,
        CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, Guid>();
        return plan.FetchForWriting(_session, id, true, cancellation);
    }

    public Task<IEventStream<T>> FetchForExclusiveWriting<T>(string key,
        CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, string>();
        return plan.FetchForWriting(_session, key, true, cancellation);
    }

    public ValueTask<T?> FetchLatest<T>(Guid id, CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, Guid>();
        return plan.FetchForReading(_session, id, cancellation);
    }

    public ValueTask<T?> FetchLatest<T>(string id, CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, string>();
        return plan.FetchForReading(_session, id, cancellation);
    }

    public Task<bool> StreamLatestJson<T>(Guid id, Stream destination, CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, Guid>();
        return plan.StreamForReading(_session, id, destination, cancellation);
    }

    public Task<bool> StreamLatestJson<T>(string id, Stream destination, CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, string>();
        return plan.StreamForReading(_session, id, destination, cancellation);
    }

    internal IAggregateFetchPlan<TDoc, TId> FindFetchPlan<TDoc, TId>() where TDoc : class where TId : notnull
    {
        if (typeof(TId) == typeof(Guid))
        {
            _session.Options.EventGraph.EnsureAsGuidStorage(_session);
        }
        else if (typeof(TId) == typeof(string))
        {
            _session.Options.EventGraph.EnsureAsStringStorage(_session);
        }
        // else: natural key type — event storage initialization deferred to the plan

        // Use (TDoc, TId) as cache key to support both stream id and natural key lookups
        var cacheKey = (typeof(TDoc), typeof(TId));
        if (_fetchStrategies.TryFind(cacheKey, out var stored))
        {
            return (IAggregateFetchPlan<TDoc, TId>)stored;
        }

        var plan = determineFetchPlan<TDoc, TId>(_session.Options);

        _fetchStrategies = _fetchStrategies.AddOrUpdate(cacheKey, plan);

        return plan;
    }

    private IAggregateFetchPlan<TDoc, TId> determineFetchPlan<TDoc, TId>(StoreOptions options) where TDoc : class where TId : notnull
    {
        // For natural key types (not Guid/string), try natural key planners first
        // before attempting the cast to IEventIdentityStrategy<TId>
        if (typeof(TId) != typeof(Guid) && typeof(TId) != typeof(string))
        {
            // Auto-discover natural key from [NaturalKey] attribute on the aggregate type
            // BEFORE iterating planners, so the projection is registered and available
            tryAutoRegisterNaturalKeyProjection<TDoc, TId>(options);

            foreach (var planner in options.Projections.allPlanners())
            {
                // Pass null identity - natural key planners don't use it
                if (planner.TryMatch<TDoc, TId>(null!, options, out var naturalKeyPlan))
                {
                    return naturalKeyPlan;
                }
            }
        }
        else
        {
            foreach (var planner in options.Projections.allPlanners())
            {
                if (planner.TryMatch<TDoc, TId>((IEventIdentityStrategy<TId>)this, options, out var plan))
                {
                    return plan;
                }
            }
        }

        throw new InvalidOperationException(
            $"Unable to determine a fetch plan for aggregate {typeof(TDoc).FullNameInCode()}. Is there a valid single stream aggregation projection for this type?");
    }

    /// <summary>
    /// Auto-discovers a natural key from [NaturalKey] attribute on the aggregate type
    /// and registers an Inline snapshot projection if no projection exists yet.
    /// This enables FetchForWriting with natural keys on self-aggregating types
    /// without requiring explicit projection registration.
    /// </summary>
    private static void tryAutoRegisterNaturalKeyProjection<TDoc, TId>(StoreOptions options)
        where TDoc : class where TId : notnull
    {
        // Skip if a projection is already registered for this aggregate type
        if (options.Projections.TryFindAggregate(typeof(TDoc), out _))
        {
            return;
        }

        var naturalKeyProp = typeof(TDoc).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<NaturalKeyAttribute>() != null);

        if (naturalKeyProp == null || naturalKeyProp.PropertyType != typeof(TId))
        {
            return;
        }

        // Register an Inline snapshot projection so the natural key infrastructure
        // (natural key table, inline projection, NaturalKeyFetchPlanner) all activate
        options.Projections.Snapshot<TDoc>(SnapshotLifecycle.Inline);
    }
}

public interface IAggregateFetchPlan<TDoc, in TId> where TDoc : notnull
{
    ProjectionLifecycle Lifecycle { get; }

    Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default);

    Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation = default);

    ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TId id, CancellationToken cancellation);

    Task<bool> StreamForReading(DocumentSessionBase session, TId id, Stream destination, CancellationToken cancellation);

    // These two methods are for batching
    IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id,
        long expectedStartingVersion);

    IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate);

    IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TId id);
}

public interface IEventIdentityStrategy<in TId>
{
    Task<IEventStorage> EnsureEventStorageExists<T>(DocumentSessionBase session, CancellationToken cancellation);
    void BuildCommandForReadingVersionForStream(bool isGlobal, ICommandBuilder builder, TId id, bool forUpdate);

    IEventStream<TDoc> StartStream<TDoc>(TDoc? document, DocumentSessionBase session, TId id,
        CancellationToken cancellation) where TDoc : class;

    IEventStream<TDoc> AppendToStream<TDoc>(TDoc? document, DocumentSessionBase session, TId id, long version,
        CancellationToken cancellation) where TDoc : class;

    IQueryHandler<IReadOnlyList<IEvent>> BuildEventQueryHandler(bool isGlobal, TId id, IEventStorage eventStorage,
        ISqlFragment? filter = null);

    IQueryHandler<IReadOnlyList<IEvent>> BuildEventQueryHandler(bool isGlobal, TId id,
        ISqlFragment? filter = null);
}
