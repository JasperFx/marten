using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Internal.Operations;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

public class StatementMap<T>: IEventHandler
{
    private readonly List<IColumnMap> _columnMaps = new();
    private readonly FlatTableProjection _parent;
    private readonly MemberInfo[] _pkMembers;
    private DbObjectName _functionIdentifier;

    // Setters are realized in Compile() so they can read StoreOptions
    // (DuplicatedFieldEnumStorage and registered value types) off the EventGraph.
    // The pending list captures only the member arrays declared via Map / Increment /
    // Decrement; the leading PK setter is added in Compile.
    private readonly List<MemberInfo[]> _pendingMemberSetters = new();
    private IParameterSetter<IEvent>[] _setters = Array.Empty<IParameterSetter<IEvent>>();
    private string _sql;
    private readonly bool _streamIdentified;

    public StatementMap(FlatTableProjection parent, MemberInfo[] pkMembers)
    {
        _parent = parent;
        _pkMembers = pkMembers;

        _streamIdentified = pkMembers.IsEmpty();
    }

    Type IEventHandler.EventType => typeof(T);

    bool IEventHandler.AssertValid(EventGraph events, out string? message)
    {
        message = null;
        return true;
    }

    IEnumerable<ISchemaObject> IEventHandler.BuildObjects(EventGraph events, Table table)
    {
        createFunctionName(table);

        yield return new FlatTableUpsertFunction(_functionIdentifier, table, _columnMaps);
    }

    public void Compile(EventGraph events, Table table)
    {
        var storeOptions = events.Options;
        var setters = new List<IParameterSetter<IEvent>>(_pendingMemberSetters.Count + 1);

        // Primary-key setter is always at column index 0.
        if (_streamIdentified)
        {
            var setter = events.StreamIdentity == StreamIdentity.AsGuid
                ? (IParameterSetter<IEvent>)new ParameterSetter<IEvent, Guid>(e => e.StreamId)
                : new ParameterSetter<IEvent, string>(e => e.StreamKey);
            setters.Add(setter);
        }
        else
        {
            setters.Add(FlatTableProjection.BuildPrimaryKeySetter<T>(_pkMembers, storeOptions));
        }

        foreach (var members in _pendingMemberSetters)
        {
            setters.Add(FlatTableProjection.BuildSetterForMembers<T>(members, storeOptions));
        }

        _setters = setters.ToArray();

        createFunctionName(table);
        var parameters = _setters.Select(_ => "?").Join(", ");
        _sql = $"select {_functionIdentifier}({parameters})";
    }

    public void Handle(IDocumentOperations operations, IEvent e)
    {
        var op = new SqlOperation(_sql, e, _setters);
        operations.QueueOperation(op);
    }

    private void createFunctionName(Table table)
    {
        var functionName = $"mt_upsert_{table.Identifier.Name.ToLower()}_{typeof(T).NameInCode().ToLower().Sanitize()}";
        _functionIdentifier = new PostgresqlObjectName(table.Identifier.Schema, functionName);
    }


    /// <summary>
    ///     Map a single value in the event data to a column in the table
    /// </summary>
    /// <param name="members"></param>
    /// <param name="columnName">Explicitly define the column name, otherwise this will be derived from the members</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public Table.ColumnExpression Map<TValue>(Expression<Func<T, TValue>> members, string? columnName = null)
    {
        var map = new MemberMap<T, TValue>(members, columnName, ColumnMapType.Value);
        _columnMaps.Add(map);

        _pendingMemberSetters.Add(map.Members);

        return map.ResolveColumn(_parent.Table);
    }


    /// <summary>
    ///     Directs the projection to increment the designated column by the value of the event data values
    /// </summary>
    /// <param name="members"></param>
    /// <param name="columnName"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public Table.ColumnExpression Increment<TValue>(Expression<Func<T, TValue>> members, string? columnName = null)
    {
        var map = new MemberMap<T, TValue>(members, columnName, ColumnMapType.Increment);
        _columnMaps.Add(map);

        _pendingMemberSetters.Add(map.Members);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Directs the projection to increment by one the value of the designated column
    /// </summary>
    /// <param name="columnName"></param>
    /// <returns></returns>
    public Table.ColumnExpression Increment(string columnName)
    {
        var map = new IncrementMap(columnName);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Directs the projection to decrement the designated column by the value of the event data values
    /// </summary>
    /// <param name="columnName"></param>
    /// <returns></returns>
    public Table.ColumnExpression Decrement<TValue>(Expression<Func<T, TValue>> members, string? columnName = null)
    {
        var map = new MemberMap<T, TValue>(members, columnName, ColumnMapType.Decrement);
        _columnMaps.Add(map);

        _pendingMemberSetters.Add(map.Members);


        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Directs the projection to decrement by one the value of the designated column
    /// </summary>
    /// <param name="columnName"></param>
    /// <returns></returns>
    public Table.ColumnExpression Decrement(string columnName)
    {
        var map = new DecrementMap(columnName);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Set the designated column value to the explicit string value when this event type is encountered
    /// </summary>
    /// <param name="columnName"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public Table.ColumnExpression SetValue(string columnName, string value)
    {
        var map = new SetStringValueMap(columnName, value);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Set the designated column value to the explicit integer value when this event type is encountered
    /// </summary>
    /// <param name="columnName"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public Table.ColumnExpression SetValue(string columnName, int value)
    {
        var map = new SetIntValueMap(columnName, value);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }
}
