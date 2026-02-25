using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Fetching;
using StreamState = Marten.Events.StreamState;
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
        await Parent.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);

        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, Guid>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, id, true);

        return await AddItem(handler).ConfigureAwait(false);
    }

    public async Task<IEventStream<T>> FetchForExclusiveWriting<T>(string key) where T : class
    {
        await Parent.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);

        _documentTypes.Add(typeof(IEvent));
        var plan = Parent.Events.As<EventStore>().FindFetchPlan<T, string>();
        if (plan.Lifecycle != ProjectionLifecycle.Live)
        {
            _documentTypes.Add(typeof(T));
        }
        var handler = plan.BuildQueryHandler(Parent, key, true);
        return await AddItem(handler).ConfigureAwait(false);
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
}
