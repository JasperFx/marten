using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JasperFx.Events;

namespace Marten.Events;

public interface IEventStream<out T> where T: notnull
{
    T? Aggregate { get; }
    long? StartingVersion { get; }
    long? CurrentVersion { get; }

    CancellationToken Cancellation { get; }

    public Guid Id { get; }
    public string Key { get; }

    IReadOnlyList<IEvent> Events { get; }
    void AppendOne(object @event);
    void AppendMany(params object[] events);
    void AppendMany(IEnumerable<object> events);
}

internal class EventStream<T>: IEventStream<T> where T: notnull
{
    private readonly StreamAction _stream;
    private readonly Func<object, IEvent> _wrapper;

    public EventStream(EventGraph events, Guid streamId, T? aggregate, CancellationToken cancellation,
        StreamAction stream)
    {
        _wrapper = o =>
        {
            var e = events.BuildEvent(o);
            e.StreamId = streamId;
            return e;
        };

        _stream = stream;
        _stream.AggregateType = typeof(T);

        Cancellation = cancellation;
        Aggregate = aggregate;
    }

    public EventStream(EventGraph events, string streamKey, T? aggregate, CancellationToken cancellation,
        StreamAction stream)
    {
        _wrapper = o =>
        {
            var e = events.BuildEvent(o);
            e.StreamKey = streamKey;
            return e;
        };

        _stream = stream;
        _stream.AggregateType = typeof(T);

        Cancellation = cancellation;
        Aggregate = aggregate;
    }

    public Guid Id => _stream.Id;
    public string Key => _stream.Key;

    public T? Aggregate { get; }
    public long? StartingVersion => _stream.ExpectedVersionOnServer;

    public long? CurrentVersion => _stream.ExpectedVersionOnServer == null
        ? null
        : _stream.ExpectedVersionOnServer.Value + _stream.Events.Count;

    public void AppendOne(object @event)
    {
        _stream.AddEvent(_wrapper(@event));
    }

    public void AppendMany(params object[] events)
    {
        _stream.AddEvents(events.Select(e => _wrapper(e)).ToArray());
    }

    public void AppendMany(IEnumerable<object> events)
    {
        _stream.AddEvents(events.Select(e => _wrapper(e)).ToArray());
    }

    public CancellationToken Cancellation { get; }

    public IReadOnlyList<IEvent> Events => _stream.Events;
}
