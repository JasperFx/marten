using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Marten.Internal.Sessions;

namespace Marten.Events;

public interface IEventStream<T>
{
    T Aggregate { get; }
    long? StartingVersion { get; }
    long? CurrentVersion { get; }

    CancellationToken Cancellation { get; }

    public Guid Id { get; }
    public string Key { get; }

    IReadOnlyList<IEvent> Events { get; }
    void AppendOne(object @event);
    void AppendMany(params object[] events);
    void AppendMany(IEnumerable<object> events);

    /// <summary>
    /// If you need to reuse this IEventStream after committing the active
    /// unit of work, call this method to "forward" the expected version
    /// for the next usage in a subsequent unit of work.
    ///
    /// This will do no harm if the event stream has never been committed and can
    /// be used safely regardless of the EventStream state
    ///
    /// This is probably mostly useful for legacy code
    /// </summary>
    void TryFastForwardVersion();
}

internal interface IEventStream
{

}

internal class EventStream<T>: IEventStream<T>, IEventStream
{
    private readonly DocumentSessionBase _session;
    private StreamAction _stream;
    private readonly Func<object, IEvent> _wrapper;

    public EventStream(DocumentSessionBase session, EventGraph events, Guid streamId, T aggregate, CancellationToken cancellation,
        StreamAction stream)
    {
        _wrapper = o =>
        {
            var e = events.BuildEvent(o);
            e.StreamId = streamId;
            return e;
        };

        _session = session;
        _stream = stream;
        _stream.AggregateType = typeof(T);

        Cancellation = cancellation;
        Aggregate = aggregate;
    }

    public EventStream(DocumentSessionBase session, EventGraph events, string streamKey, T aggregate, CancellationToken cancellation,
        StreamAction stream)
    {
        _wrapper = o =>
        {
            var e = events.BuildEvent(o);
            e.StreamKey = streamKey;
            return e;
        };

        _session = session;
        _stream = stream;
        _stream.AggregateType = typeof(T);

        Cancellation = cancellation;
        Aggregate = aggregate;
    }

    public void TryFastForwardVersion()
    {
        if (_session.WorkTracker.Streams.Contains(_stream))
        {
            return;
        }

        _stream = _stream.FastForward();
        _session.WorkTracker.Streams.Add(_stream);
    }

    public Guid Id => _stream.Id;
    public string Key => _stream.Key;

    public T Aggregate { get; }
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
