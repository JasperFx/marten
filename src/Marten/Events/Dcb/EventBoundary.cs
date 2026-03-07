using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events;
using JasperFx.Events.Tags;
using JasperFx.Core;
using Marten.Internal.Sessions;

namespace Marten.Events.Dcb;

internal class EventBoundary<T>: IEventBoundary<T> where T : notnull
{
    private readonly DocumentSessionBase _session;
    private readonly EventGraph _events;

    public EventBoundary(DocumentSessionBase session, EventGraph events, T? aggregate,
        IReadOnlyList<IEvent> loadedEvents, long lastSeenSequence)
    {
        _session = session;
        _events = events;
        Aggregate = aggregate;
        Events = loadedEvents;
        LastSeenSequence = lastSeenSequence;
    }

    public T? Aggregate { get; }
    public long LastSeenSequence { get; }
    public IReadOnlyList<IEvent> Events { get; }

    public void AppendOne(object @event)
    {
        var wrapped = _events.BuildEvent(@event);
        RouteEventByTags(wrapped);
    }

    public void AppendMany(params object[] events)
    {
        foreach (var e in events)
        {
            AppendOne(e);
        }
    }

    public void AppendMany(IEnumerable<object> events)
    {
        foreach (var e in events)
        {
            AppendOne(e);
        }
    }

    private void RouteEventByTags(IEvent wrapped)
    {
        var tags = wrapped.Tags;

        // If no explicit tags, try to infer from event properties
        if (tags == null || tags.Count == 0)
        {
            var inferred = EventTagInference.InferTags(wrapped.Data, _events.TagTypes);
            if (inferred.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot route event of type '{wrapped.Data.GetType().Name}' appended via IEventBoundary. " +
                    "The event has no explicit tags set via WithTag() and Marten could not infer any tags " +
                    "from its public properties matching registered tag types. Either set tags explicitly " +
                    "or ensure the event type has properties matching registered tag types.");
            }

            foreach (var tag in inferred)
            {
                wrapped.AddTag(tag);
            }

            tags = wrapped.Tags;
        }

        // Find the stream to route to. An event belongs to exactly ONE stream,
        // but its tags are written to ALL matching tag tables at save time.
        // Use the first tag with an AggregateType to determine the target stream.
        StreamAction? stream = null;

        foreach (var tag in tags!)
        {
            var registration = _events.FindTagType(tag.TagType);
            if (registration?.AggregateType == null) continue;

            var streamId = tag.Value;
            if (streamId is Guid guidId)
            {
                if (!_session.WorkTracker.TryFindStream(guidId, out stream))
                {
                    stream = StreamAction.Append(guidId, new[] { wrapped });
                    stream.AggregateType = registration.AggregateType;
                    _session.WorkTracker.Streams.Add(stream);
                }
                else
                {
                    stream.AddEvent(wrapped);
                }
            }
            else if (streamId is string stringId)
            {
                if (!_session.WorkTracker.TryFindStream(stringId, out stream))
                {
                    stream = StreamAction.Append(stringId, new[] { wrapped });
                    stream.AggregateType = registration.AggregateType;
                    _session.WorkTracker.Streams.Add(stream);
                }
                else
                {
                    stream.AddEvent(wrapped);
                }
            }

            break; // Route to the first matching stream only
        }

        // If no tag has an AggregateType, create a new orphan stream
        // to avoid concurrency conflicts
        if (stream == null)
        {
            if (_events.StreamIdentity == StreamIdentity.AsGuid)
            {
                var newId = CombGuidIdGeneration.NewGuid();
                stream = StreamAction.Start(newId, new[] { wrapped });
                _session.WorkTracker.Streams.Add(stream);
            }
            else
            {
                var newKey = Guid.NewGuid().ToString();
                stream = StreamAction.Start(newKey, new[] { wrapped });
                _session.WorkTracker.Streams.Add(stream);
            }
        }

        if (stream.Id != Guid.Empty)
        {
            wrapped.StreamId = stream.Id;
        }
        else if (stream.Key != null)
        {
            wrapped.StreamKey = stream.Key;
        }
    }
}
