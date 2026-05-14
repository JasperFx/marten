using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JasperFx.Events;
using Marten.Internal.Sessions;

namespace Marten.Events;

/// <summary>
/// Internal marker interface for event streams.
/// </summary>
internal interface IEventStream
{
    void TryFastForwardVersion();
}

// NOTE: The public IEventStream<T> contract moved to JasperFx.Events.IEventStream<T>
// as part of the Marten 9 dedupe pillar consumption. Code that previously imported
// Marten.Events.IEventStream<T> now resolves the unqualified `IEventStream<T>` to
// the JasperFx.Events version via `using JasperFx.Events;`. See the migration guide
// for the namespace move.

internal class EventStream<T>: IEventStream<T>, IEventStream where T: notnull
{
    private StreamAction _stream;
    private readonly Func<object, IEvent> _wrapper;
    private readonly DocumentSessionBase _session;

    public EventStream(DocumentSessionBase session, EventGraph events, Guid streamId, T? aggregate, CancellationToken cancellation,
        StreamAction stream)
    {
        _session = session;
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

    public EventStream(DocumentSessionBase session, EventGraph events, string streamKey, T? aggregate, CancellationToken cancellation,
        StreamAction stream)
    {
        _session = session;
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
    public string? Key => _stream.Key;

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

    public bool AlwaysEnforceConsistency
    {
        get => _stream.AlwaysEnforceConsistency;
        set => _stream.AlwaysEnforceConsistency = value;
    }

    public IReadOnlyList<IEvent> Events => _stream.Events;

    public void TryFastForwardVersion()
    {
        if (_session.WorkTracker.Streams.Contains(_stream))
        {
            return;
        }

        _stream = _stream.FastForward();
        _session.WorkTracker.Streams.Add(_stream);
    }
}
