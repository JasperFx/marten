using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using Marten.Linq.Parsing;
using Marten.Util;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal class MemberMap<TEvent, TMember>: IColumnMap
{
    private readonly ColumnMapType _mapType;
    private readonly MemberInfo[] _members;
    private TableColumn? _tableColumn;

    public MemberMap(Expression<Func<TEvent, TMember>> members, string? tableColumn,
        ColumnMapType columnMapType)
    {
        _members = MemberFinder.Determine(members);
        _mapType = columnMapType;

        ColumnName = tableColumn ?? _members.Select(x => x.Name.ToSnakeCase()).Join("_");
    }

    public MemberInfo[] Members => _members;

    public string ColumnName { get; }

    public Table.ColumnExpression ResolveColumn(Table table)
    {
        _tableColumn = table.ColumnFor(ColumnName);

        return _tableColumn == null
            ? table.AddColumn<TMember>(ColumnName)
            : new Table.ColumnExpression(table, _tableColumn);
    }

    public string UpdateFieldSql(Table table)
    {
        _tableColumn ??= table.ColumnFor(ColumnName);


        return _mapType switch
        {
            ColumnMapType.Increment => $"{ColumnName} = {table.Identifier.Name}.{ColumnName} + {_tableColumn.ToArgumentName()}",
            ColumnMapType.Value => $"{ColumnName} = {_tableColumn.ToArgumentName()}",
            ColumnMapType.Decrement => $"{ColumnName} = {table.Identifier.Name}.{ColumnName} - {_tableColumn.ToArgumentName()}",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    public bool RequiresInput => true;

    public string ToInsertExpression(Table table)
    {
        return _mapType switch
        {
            ColumnMapType.Increment or ColumnMapType.Value => table.ColumnFor(ColumnName).ToArgumentName(),
            ColumnMapType.Decrement => "-" + table.ColumnFor(ColumnName).ToArgumentName(),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

}
