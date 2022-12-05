#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events;

internal partial class EventStore
{
    public StreamAction Append(Guid stream, IEnumerable<object> events)
    {
        //TODO NRT: We're ignoring null here as to not unintentionally change any downstream behaviour - Replace with null guards in the future.
        return Append(stream, events?.ToArray()!);
    }

    public StreamAction Append(Guid stream, params object[] events)
    {
        return _store.Events.Append(_session, stream, events);
    }

    public StreamAction Append(string stream, IEnumerable<object> events)
    {
        return Append(stream, events?.ToArray()!);
    }

    public StreamAction Append(string stream, params object[] events)
    {
        return _store.Events.Append(_session, stream, events);
    }

    public StreamAction Append(Guid stream, long expectedVersion, IEnumerable<object> events)
    {
        return Append(stream, expectedVersion, events?.ToArray()!);
    }

    public StreamAction Append(Guid stream, long expectedVersion, params object[] events)
    {
        var eventStream = Append(stream, events);
        eventStream.ExpectedVersionOnServer = expectedVersion - eventStream.Events.Count;

        return eventStream;
    }

    public StreamAction Append(string stream, long expectedVersion, IEnumerable<object> events)
    {
        return Append(stream, expectedVersion, events?.ToArray()!);
    }

    public StreamAction Append(string stream, long expectedVersion, params object[] events)
    {
        var eventStream = Append(stream, events);
        eventStream.ExpectedVersionOnServer = expectedVersion - eventStream.Events.Count;

        return eventStream;
    }
}
