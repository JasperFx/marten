#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public interface IMartenEventSlicer<TDoc, TId>
{
    /// <summary>
    ///     This is called by the asynchronous projection runner
    /// </summary>
    /// <param name="querySession"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    ValueTask<IReadOnlyList<EventSliceGroup<TDoc, TId>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events);
}

internal class MartenEventSlicerAdapter<TDoc, TId> : IEventSlicer<TDoc, TId>
{
    private readonly IDocumentStore _store;
    private readonly IMartenDatabase _database;
    private readonly IMartenEventSlicer<TDoc, TId> _slicer;

    public MartenEventSlicerAdapter(IDocumentStore store, IMartenDatabase database, IMartenEventSlicer<TDoc, TId> slicer)
    {
        _store = store;
        _database = database;
        _slicer = slicer;
    }

    public async ValueTask<IReadOnlyList<JasperFx.Events.Grouping.EventSliceGroup<TDoc, TId>>> SliceAsyncEvents(
        List<IEvent> events)
    {
        await using var session = _store.QuerySession(SessionOptions.ForDatabase(_database));
        return await _slicer.SliceAsyncEvents(session, events).ConfigureAwait(false);
    }
}
