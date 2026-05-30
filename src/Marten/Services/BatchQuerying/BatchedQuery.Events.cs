using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Events.Dcb;
using Marten.Events.Fetching;
using StreamState = JasperFx.Events.StreamState;
using Marten.Events.Querying;
using Marten.Linq.QueryHandlers;

namespace Marten.Services.BatchQuerying;

internal partial class BatchedQuery: IBatchEvents
{
    public Task<IEvent> Load(Guid id)
    {
        _documentTypes.Add(typeof(IEvent));
        var handler = new SingleEventQueryHandler(id, Parent.EventStorage());
        return AddItem(handler);
    }

    public Task<StreamState> FetchStreamState(Guid streamId)
    {
        _documentTypes.Add(typeof(IEvent));
        var handler = Parent.EventStorage()
            .QueryForStream(StreamAction.ForReference(streamId, Parent.TenantId));

        return AddItem(handler);
    }

    public Task<StreamState> FetchStreamState(string streamKey)
    {
        _documentTypes.Add(typeof(IEvent));
        var handler = Parent.EventStorage()
            .QueryForStream(StreamAction.ForReference(streamKey, Parent.TenantId));

        return AddItem(handler);
    }

    public Task<IReadOnlyList<IEvent>> FetchStream(Guid streamId, long version = 0, DateTimeOffset? timestamp = null,
        long fromVersion = 0)
    {
        _documentTypes.Add(typeof(IEvent));
        var selector = Parent.EventStorage();
        var statement = new EventStatement(selector, Parent.Options.EventGraph)
        {
            StreamId = streamId,
            Version = version,
            Timestamp = timestamp,
            TenantId = Parent.TenantId,
            FromVersion = fromVersion
        };

        IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

        return AddItem(handler);
    }

    public Task<IReadOnlyList<IEvent>> FetchStream(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        long fromVersion = 0)
    {
        _documentTypes.Add(typeof(IEvent));
        var selector = Parent.EventStorage();
        var statement = new EventStatement(selector, Parent.Options.EventGraph)
        {
            StreamKey = streamKey,
            Version = version,
            Timestamp = timestamp,
            TenantId = Parent.TenantId,
            FromVersion = fromVersion
        };

        IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

        return AddItem(handler);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(Guid id) where T : class
    {
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, Guid>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, id, false);
        return AddItem(handler);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(string key) where T : class
    {
        _documentTypes.Add(typeof(IEvent));

        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, string>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }

        var handler = plan.BuildQueryHandler(Parent, key, false);
        return AddItem(handler);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(Guid id, long expectedVersion) where T : class
    {
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, Guid>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, id, expectedVersion);
        return AddItem(handler);
    }

    public Task<IEventStream<T>> FetchForWriting<T>(string key, long expectedVersion) where T : class
    {
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, string>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, key, expectedVersion);
        return AddItem(handler);
    }

    public async Task<IEventStream<T>> FetchForExclusiveWriting<T>(Guid id) where T : class
    {
        // Enlist synchronously BEFORE the first await so the item is in _items
        // by the time control returns to the caller. A subsequent Execute() is
        // then guaranteed to see and process the item.
        //
        // Previously, `await Parent.BeginTransactionAsync(...)` ran first. Under
        // concurrency BeginTransactionAsync does not complete synchronously
        // (AutoClosingLifetime.StartAsync performs a real socket round-trip in
        // NpgsqlConnection.OpenAsync), so the method yielded before AddItem ran.
        // The codegen pattern `var t = batch.Events.FetchForExclusiveWriting(id);
        // await batch.Execute(ct); var s = await t;` then called Execute with an
        // empty _items list, returned immediately, and the item.Result was never
        // populated — causing the awaiter on `t` to wedge forever.
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, Guid>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, id, true);
        var resultTask = AddItem(handler);

        await Parent.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        return await resultTask.ConfigureAwait(false);
    }

    public async Task<IEventStream<T>> FetchForExclusiveWriting<T>(string key) where T : class
    {
        // See the Guid overload above for the explanation — enlist synchronously
        // before the first await to avoid the async-vs-sync-enlistment race.
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, string>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, key, true);
        var resultTask = AddItem(handler);

        await Parent.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        return await resultTask.ConfigureAwait(false);
    }

    public Task<T?> FetchLatest<T>(Guid id) where T : class
    {
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, Guid>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, id);
        return AddItem(handler);
    }

    public Task<T?> FetchLatest<T>(string id) where T : class
    {
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, string>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, id);
        return AddItem(handler);
    }

    public Task<bool> EventsExist(EventTagQuery query)
    {
        _documentTypes.Add(typeof(IEvent));
        var store = (DocumentStore)Parent.DocumentStore;
        var handler = new EventsExistByTagsHandler(store, query);
        return AddItem(handler);
    }

    public Task<IEventBoundary<T>> FetchForWritingByTags<T>(EventTagQuery query) where T : class
    {
        Parent.AssertIsDocumentSession();
        _documentTypes.Add(typeof(IEvent));
        var store = (DocumentStore)Parent.DocumentStore;
        var handler = new FetchForWritingByTagsHandler<T>(store, query);
        return AddItem(handler);
    }
}
