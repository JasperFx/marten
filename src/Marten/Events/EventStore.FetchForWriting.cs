using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events;

internal partial class EventStore: IEventIdentityStrategy<Guid>, IEventIdentityStrategy<string>
{
    private ImHashMap<Type, object> _fetchStrategies = ImHashMap<Type, object>.Empty;

    async Task<IEventStorage> IEventIdentityStrategy<Guid>.EnsureEventStorageExists<T>(
        DocumentSessionBase session, CancellationToken cancellation)
    {
        var selector = _store.Events.EnsureAsGuidStorage(_session);
        await session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);

        return selector;
    }

    IEventStream<TDoc> IEventIdentityStrategy<Guid>.StartStream<TDoc>(TDoc document, DocumentSessionBase session,
        Guid id, CancellationToken cancellation) where TDoc : class
    {
        var action = _store.Events.StartEmptyStream(session, id);
        action.AggregateType = typeof(TDoc);
        action.ExpectedVersionOnServer = 0;

        return new EventStream<TDoc>(_store.Events, id, document, cancellation, action);
    }

    IEventStream<TDoc> IEventIdentityStrategy<Guid>.AppendToStream<TDoc>(TDoc document, DocumentSessionBase session,
        Guid id, long version, CancellationToken cancellation)
    {
        var action = session.Events.Append(id);
        action.ExpectedVersionOnServer = version;
        return new EventStream<TDoc>(_store.Events, id, document, cancellation, action);
    }

    IQueryHandler<IReadOnlyList<IEvent>> IEventIdentityStrategy<Guid>.BuildEventQueryHandler(Guid id,
        IEventStorage selector, ISqlFragment? filter = null)
    {
        var statement = new EventStatement(selector) { StreamId = id, TenantId = _tenant.TenantId };
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

    IEventStream<TDoc> IEventIdentityStrategy<string>.StartStream<TDoc>(TDoc document, DocumentSessionBase session,
        string id, CancellationToken cancellation) where TDoc : class
    {
        var action = _store.Events.StartEmptyStream(session, id);
        action.AggregateType = typeof(TDoc);
        action.ExpectedVersionOnServer = 0;

        return new EventStream<TDoc>(_store.Events, id, document, cancellation, action);
    }

    IEventStream<TDoc> IEventIdentityStrategy<string>.AppendToStream<TDoc>(TDoc document,
        DocumentSessionBase session, string id, long version, CancellationToken cancellation)
    {
        var action = session.Events.Append(id);
        action.ExpectedVersionOnServer = version;
        return new EventStream<TDoc>(_store.Events, id, document, cancellation, action);
    }

    IQueryHandler<IReadOnlyList<IEvent>> IEventIdentityStrategy<string>.BuildEventQueryHandler(string id,
        IEventStorage selector, ISqlFragment? filter = null)
    {
        var statement = new EventStatement(selector) { StreamKey = id, TenantId = _tenant.TenantId };
        if (filter != null)
        {
            statement.Filters = [filter];
        }

        return new ListQueryHandler<IEvent>(statement, selector);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(Guid id, CancellationToken cancellation = default) where T : class
    {
        var plan = findFetchPlan<T, Guid>();
        return plan.FetchForWriting(_session, id, false, cancellation);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(string key, CancellationToken cancellation = default)
        where T : class
    {
        var plan = findFetchPlan<T, string>();
        return plan.FetchForWriting(_session, key, false, cancellation);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(Guid id, long initialVersion,
        CancellationToken cancellation = default) where T : class
    {
        var plan = findFetchPlan<T, Guid>();
        return plan.FetchForWriting(_session, id, initialVersion, cancellation);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(string key, long initialVersion,
        CancellationToken cancellation = default) where T : class
    {
        var plan = findFetchPlan<T, string>();
        return plan.FetchForWriting(_session, key, initialVersion, cancellation);
    }

    public Task<IEventStream<T>> FetchForExclusiveWriting<T>(Guid id,
        CancellationToken cancellation = default) where T : class
    {
        var plan = findFetchPlan<T, Guid>();
        return plan.FetchForWriting(_session, id, true, cancellation);
    }

    public Task<IEventStream<T>> FetchForExclusiveWriting<T>(string key,
        CancellationToken cancellation = default) where T : class
    {
        var plan = findFetchPlan<T, string>();
        return plan.FetchForWriting(_session, key, true, cancellation);
    }

    private IAggregateFetchPlan<TDoc, TId> findFetchPlan<TDoc, TId>() where TDoc : class
    {
        if (typeof(TId) == typeof(Guid))
        {
            _session.Options.EventGraph.EnsureAsGuidStorage(_session);
        }
        else
        {
            _session.Options.EventGraph.EnsureAsStringStorage(_session);
        }

        if (_fetchStrategies.TryFind(typeof(TDoc), out var stored))
        {
            return (IAggregateFetchPlan<TDoc, TId>)stored;
        }

        // All the IDocumentStorage types are codegen'd
        // ReSharper disable once SuspiciousTypeConversion.Global
        var documentProvider = _store.Options.Providers.StorageFor<TDoc>();
        var storage = (IDocumentStorage<TDoc, TId>)documentProvider.IdentityMap;

        var plan = determineFetchPlan(storage, _session.Options);

        _fetchStrategies = _fetchStrategies.AddOrUpdate(typeof(TDoc), plan);

        return plan;
    }

    private IAggregateFetchPlan<TDoc, TId> determineFetchPlan<TDoc, TId>(IDocumentStorage<TDoc, TId> storage,
        StoreOptions options) where TDoc : class
    {
        foreach (var planner in options.Projections.allPlanners())
        {
            if (planner.TryMatch(storage, (IEventIdentityStrategy<TId>)this, options, out var plan)) return plan;
        }

        throw new ArgumentOutOfRangeException(nameof(storage),
            $"Unable to determine a fetch plan for aggregate {typeof(TDoc).FullNameInCode()}");
    }
}

public interface IAggregateFetchPlan<TDoc, TId>
{
    Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default);

    Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation = default);
}

public interface IEventIdentityStrategy<TId>
{
    Task<IEventStorage> EnsureEventStorageExists<T>(DocumentSessionBase session, CancellationToken cancellation);
    void BuildCommandForReadingVersionForStream(ICommandBuilder builder, TId id, bool forUpdate);

    IEventStream<TDoc> StartStream<TDoc>(TDoc document, DocumentSessionBase session, TId id,
        CancellationToken cancellation) where TDoc : class;

    IEventStream<TDoc> AppendToStream<TDoc>(TDoc document, DocumentSessionBase session, TId id, long version,
        CancellationToken cancellation);

    IQueryHandler<IReadOnlyList<IEvent>> BuildEventQueryHandler(TId id, IEventStorage eventStorage,
        ISqlFragment? filter = null);
}
