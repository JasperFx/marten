using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten.Events.CodeGeneration;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal class EventDeleter<T> : IEventHandler
{
    private string _sql;
    private IParameterSetter<IEvent>? _parameter;
    private readonly MemberInfo[] _pkMembers;

    public EventDeleter(MemberInfo[] members, Table table)
    {
        _pkMembers = members;
    }

    public void Compile(EventGraph events, Table table)
    {
        if (_pkMembers.Length != 0)
        {
            _parameter = FlatTableProjection.BuildPrimaryKeySetter<T>(_pkMembers, events.Options);
        }
        else
        {
            _parameter = events.StreamIdentity == StreamIdentity.AsGuid
                ? (IParameterSetter<IEvent>)new ParameterSetter<IEvent, Guid>(e => e.StreamId)
                : new ParameterSetter<IEvent, string>(e => e.StreamKey);
        }

        _sql = $"delete from {table.Identifier} where {table.PrimaryKeyColumns[0]} = ?";
    }

    public Type EventType => typeof(T);

    public void Handle(IDocumentOperations operations, IEvent e)
    {
        operations.QueueOperation(new SqlOperation(_sql, e, [_parameter]));
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
}
