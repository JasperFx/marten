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
using Marten.Storage;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events;

[UnconditionalSuppressMessage("Trimming", "IL2090",
    Justification = "Class-level: generic class type-argument flow on the aggregator / storage instantiation. Types preserved at the projection-registration boundary.")]
internal partial class EventStore: IEventIdentityStrategy<Guid>, IEventIdentityStrategy<string>
{
    // 9.0 (#4374): cache fetch plans by a small readonly struct key with stable
    // RuntimeTypeHandle-based hashing rather than a value tuple. The ImHashMap node
    // comparison would otherwise route through ValueTuple<,>.Equals which compares
    // Type references via object.Equals + boxes; this dedicated key uses reference
    // equality directly and combines the two RuntimeTypeHandle hashes for the lookup.
    private readonly struct AggregateFetchKey: IEquatable<AggregateFetchKey>
    {
        public readonly Type Aggregate;
        public readonly Type Id;

        public AggregateFetchKey(Type aggregate, Type id)
        {
            Aggregate = aggregate;
            Id = id;
        }

        public bool Equals(AggregateFetchKey other) => Aggregate == other.Aggregate && Id == other.Id;
        public override bool Equals(object? obj) => obj is AggregateFetchKey other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Aggregate),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Id));
    }

    private ImHashMap<AggregateFetchKey, object> _fetchStrategies = ImHashMap<AggregateFetchKey, object>.Empty;

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

    public ValueTask<T?> ProjectLatest<T>(Guid id, CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, Guid>();
        return plan.ProjectLatest(_session, id, cancellation);
    }

    public ValueTask<T?> ProjectLatest<T>(string id, CancellationToken cancellation = default) where T : class
    {
        var plan = FindFetchPlan<T, string>();
        return plan.ProjectLatest(_session, id, cancellation);
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
        var options = ((IMartenSession)_session).Options;

        // #5344: only assert the store's stream identity when TId is actually being used to address
        // a *stream*. A [NaturalKey] whose type happens to be Guid or string is not a stream id, and
        // asserting here refused a primitive `string` natural key on a Guid-identity store outright
        // ("This Marten event store is configured to identify streams with Guids") before any
        // planner got a look at it. A wrapped natural key never hit this because it is neither Guid
        // nor string, which is why only the primitive case was broken.
        if (!IsNaturalKeyIdentity<TDoc, TId>(options))
        {
            if (typeof(TId) == typeof(Guid))
            {
                options.EventGraph.EnsureAsGuidStorage(_session);
            }
            else if (typeof(TId) == typeof(string))
            {
                options.EventGraph.EnsureAsStringStorage(_session);
            }
            // else: natural key type — event storage initialization deferred to the plan
        }

        // Use (TDoc, TId) as cache key to support both stream id and natural key lookups
        var cacheKey = new AggregateFetchKey(typeof(TDoc), typeof(TId));
        if (_fetchStrategies.TryFind(cacheKey, out var stored))
        {
            return (IAggregateFetchPlan<TDoc, TId>)stored;
        }

        var plan = determineFetchPlan<TDoc, TId>(options);

        _fetchStrategies = _fetchStrategies.AddOrUpdate(cacheKey, plan);

        return plan;
    }

    private IAggregateFetchPlan<TDoc, TId> determineFetchPlan<TDoc, TId>(StoreOptions options) where TDoc : class where TId : notnull
    {
        // For natural key types, try natural key planners first before attempting the cast to
        // IEventIdentityStrategy<TId>. #5344: a natural key is usually a wrapper (neither Guid nor
        // string), but it can also be a primitive `string` on a Guid-identity store — in which case
        // it still is not a stream identifier, so it belongs on this branch too.
        if (IsNaturalKeyIdentity<TDoc, TId>(options) ||
            (typeof(TId) != typeof(Guid) && typeof(TId) != typeof(string)))
        {
            // Auto-discover natural key from [NaturalKey] attribute on the aggregate type
            // BEFORE iterating planners, so the projection is registered and available
            if (tryAutoRegisterNaturalKeyProjection<TDoc, TId>(options))
            {
                // The projection was just auto-registered, which adds a NaturalKeyTable
                // to the IEvent feature schema. Reset the schema existence check so
                // EnsureStorageExistsAsync(typeof(IEvent)) will re-evaluate and create
                // the natural key table.
                if (_session.Database is MartenDatabase martenDb)
                {
                    martenDb.ResetSchemaExistenceChecks();
                }
            }

            foreach (var planner in options.Projections.allPlanners())
            {
                // Pass null identity - natural key planners don't use it
                if (planner.TryMatch<TDoc, TId>(null!, options, out var naturalKeyPlan))
                {
                    return naturalKeyPlan;
                }
            }

            // #5144: not a natural key, but possibly a strong-typed identifier wrapping the stream
            // identity -- PaymentId(Guid), InvoiceId(string) and friends. That *is* the stream id,
            // so unwrap it and reuse the plan for the underlying type rather than inventing a
            // parallel one. Only Guid and string backings can address a stream.
            var valueType = options.TryFindValueType(typeof(TId));
            if (valueType != null)
            {
                if (valueType.SimpleType == typeof(Guid))
                {
                    return new UnwrappedIdentityFetchPlan<TDoc, TId, Guid>(
                        FindFetchPlan<TDoc, Guid>(), valueType.UnWrapper<TId, Guid>());
                }

                if (valueType.SimpleType == typeof(string))
                {
                    return new UnwrappedIdentityFetchPlan<TDoc, TId, string>(
                        FindFetchPlan<TDoc, string>(), valueType.UnWrapper<TId, string>());
                }

                throw new InvalidOperationException(
                    $"The strong-typed identifier {typeof(TId).FullNameInCode()} wraps {valueType.SimpleType.FullNameInCode()}, which cannot identify an event stream. Only Guid and string backed identifiers are supported here.");
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
    /// #5344: is <typeparamref name="TId"/> the aggregate's <em>natural</em> key rather than its
    /// stream identifier?
    /// </summary>
    /// <remarks>
    /// The store's own stream identity always wins, so on a string-identity store
    /// <c>FetchLatest&lt;T, string&gt;(value)</c> addresses the stream and never the natural key,
    /// exactly as it did before. What this adds is the other half: on a Guid-identity store a
    /// <c>string</c> cannot possibly name a stream, so a <c>[NaturalKey] string</c> is the only
    /// thing it can mean. Falls back to probing the attribute directly because the natural key
    /// projection may not be registered yet — <see cref="tryAutoRegisterNaturalKeyProjection{TDoc,TId}"/>
    /// runs later, inside <see cref="determineFetchPlan{TDoc,TId}"/>.
    /// </remarks>
    internal static bool IsNaturalKeyIdentity<TDoc, TId>(StoreOptions options)
        where TDoc : class where TId : notnull
    {
        var streamIdentityType = options.EventGraph.StreamIdentity == StreamIdentity.AsGuid
            ? typeof(Guid)
            : typeof(string);

        if (typeof(TId) == streamIdentityType)
        {
            return false;
        }

        if (options.Projections.TryFindAggregate(typeof(TDoc), out var projection))
        {
            return projection.NaturalKeyDefinition?.OuterType == typeof(TId);
        }

        return FindNaturalKeyProperty<TDoc>()?.PropertyType == typeof(TId);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Aggregate types reaching the fetch planners are preserved at the projection-registration boundary.")]
    private static PropertyInfo? FindNaturalKeyProperty<TDoc>() =>
        typeof(TDoc).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<NaturalKeyAttribute>() != null);

    /// <summary>
    /// Auto-discovers a natural key from [NaturalKey] attribute on the aggregate type
    /// and registers an Inline snapshot projection if no projection exists yet.
    /// This enables FetchForWriting with natural keys on self-aggregating types
    /// without requiring explicit projection registration.
    /// </summary>
    /// <returns>True if a projection was newly registered</returns>
    private static bool tryAutoRegisterNaturalKeyProjection<TDoc, TId>(StoreOptions options)
        where TDoc : class where TId : notnull
    {
        // Skip if a projection is already registered for this aggregate type
        if (options.Projections.TryFindAggregate(typeof(TDoc), out _))
        {
            return false;
        }

        var naturalKeyProp = FindNaturalKeyProperty<TDoc>();

        if (naturalKeyProp == null || naturalKeyProp.PropertyType != typeof(TId))
        {
            return false;
        }

        // Register an Inline snapshot projection so the natural key infrastructure
        // (natural key table, inline projection, NaturalKeyFetchPlanner) all activate
        options.Projections.Snapshot<TDoc>(SnapshotLifecycle.Inline);
        return true;
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

    /// <summary>
    ///     Fetch the projected aggregate including any uncommitted events in the session.
    ///     For inline projections, the updated document is also stored in the session.
    /// </summary>
    ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TId id, CancellationToken cancellation);

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
