using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using JasperFx.CodeGeneration.Model;
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


        switch (_mapType)
        {
            case ColumnMapType.Increment:
                return $"{ColumnName} = {table.Identifier.Name}.{ColumnName} + {_tableColumn.ToArgumentName()}";

            case ColumnMapType.Value:
                return $"{ColumnName} = {_tableColumn.ToArgumentName()}";

            case ColumnMapType.Decrement:
                return $"{ColumnName} = {table.Identifier.Name}.{ColumnName} - {_tableColumn.ToArgumentName()}";

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public bool RequiresInput => true;

    public string ToInsertExpression(Table table)
    {
        switch (_mapType)
        {
            case ColumnMapType.Increment:
            case ColumnMapType.Value:
                return table.ColumnFor(ColumnName).ToArgumentName();

            case ColumnMapType.Decrement:
                return "-" + table.ColumnFor(ColumnName).ToArgumentName();
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public string ToValueAccessorCode(Variable eventVariable)
    {
        var accessor = new StringBuilder();
        for(var i = 0; i < _members.Length; i++){
            accessor.Append(_members[i].Name);
            if(i < _members.Length - 1){
                if(_members[i] is PropertyInfo propertyinfo && propertyinfo.PropertyType.IsValueType && !propertyinfo.PropertyType.IsPrimitive){
                    accessor.Append('.');
                }else{
                    accessor.Append("?.");
                }
            }
        }
        return $"{eventVariable.Usage}.Data.{accessor}";
    }
}
