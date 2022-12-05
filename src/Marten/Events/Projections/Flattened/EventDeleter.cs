using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Marten.Events.CodeGeneration;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal class EventDeleter: IEventHandler
{
    private readonly MemberInfo[] _members;

    public EventDeleter(Type eventType,
        MemberInfo[] members)
    {
        EventType = eventType;
        _members = members;
    }

    public Type EventType { get; }

    public IEventHandlingFrame BuildFrame(EventGraph events, Table table)
    {
        return new DeleteRowFrame(table, EventType, determinePkMembers(events).ToArray());
    }

    public bool AssertValid(EventGraph events, out string? message)
    {
        message = null;
        return true;
    }

    public IEnumerable<ISchemaObject> BuildObjects(EventGraph events, Table table)
    {
        yield break;
    }

    private IEnumerable<MemberInfo> determinePkMembers(EventGraph events)
    {
        var wrapperType = typeof(IEvent<>).MakeGenericType(EventType);
        if (_members.Any())
        {
            yield return wrapperType.GetProperty("Data");
            foreach (var member in _members) yield return member;

            yield break;
        }

        if (events.StreamIdentity == StreamIdentity.AsGuid)
        {
            yield return typeof(IEvent).GetProperty(nameof(IEvent.StreamId));
        }
        else
        {
            yield return typeof(IEvent).GetProperty(nameof(IEvent.StreamKey));
        }
    }
}
