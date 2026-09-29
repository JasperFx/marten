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
        // Enlist synchronously so the Execute() that follows sees the item (#4590), and START the
        // session's transaction here without awaiting it: Execute() awaits it before sending anything.
        // The method stays async so an error while enlisting still arrives as a faulted task; its only
        // await is on the item, so control returns to the caller with the item enlisted.
        //
        // Awaiting it here instead handed control back to the caller while the transaction was still
        // starting whenever BeginTransactionAsync had to open a physical connection (a cold or
        // exhausted pool). The codegen pattern `var t = batch.Events.FetchForExclusiveWriting(id);
        // await batch.Execute(ct); var s = await t;` then sent the `for update` on the session's
        // auto-closing connection, outside any transaction, so the row lock was released as soon as
        // the read finished instead of being held until SaveChangesAsync.
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, Guid>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, id, true);
        var resultTask = AddItem(handler);
        startTransaction();
        return await resultTask.ConfigureAwait(false);
    }

    public async Task<IEventStream<T>> FetchForExclusiveWriting<T>(string key) where T : class
    {
        // See the Guid overload above.
        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, string>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, key, true);
        var resultTask = AddItem(handler);
        startTransaction();
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
