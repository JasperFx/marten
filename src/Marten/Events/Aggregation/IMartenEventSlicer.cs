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
    Task SliceEvents(IQuerySession querySession, IReadOnlyList<IEvent> events, SliceGroup<TDoc, TId> grouping);
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

    public async ValueTask SliceAsync(IReadOnlyList<IEvent> events, SliceGroup<TDoc, TId> grouping)
    {
        await using var session = _store.QuerySession(SessionOptions.ForDatabase(_database));
        await _slicer.SliceEvents(session, events, grouping).ConfigureAwait(false);
    }
}
