using System;
using JasperFx.CodeGeneration.Model;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal class SetStringValueMap: IColumnMap
{
    private readonly string _value;
    private TableColumn? _column;

    public SetStringValueMap(string columnName, string value)
    {
        ColumnName = columnName;
        _value = value;
    }

    public string ColumnName { get; }

    public Table.ColumnExpression ResolveColumn(Table table)
    {
        _column = table.ColumnFor(ColumnName);
        return _column == null ? table.AddColumn<string>(ColumnName) : new Table.ColumnExpression(table, _column);
    }

    public string UpdateFieldSql(Table table)
    {
        return $"{ColumnName} = '{_value}'";
    }

    public bool RequiresInput => false;

    public string ToInsertExpression(Table table)
    {
        return $"'{_value}'";
    }

    public string ToValueAccessorCode(Variable eventVariable)
    {
        throw new NotSupportedException();
    }
}

internal class SetIntValueMap: IColumnMap
{
    private readonly int _value;
    private TableColumn? _column;

    public SetIntValueMap(string columnName, int value)
    {
        ColumnName = columnName;
        _value = value;
    }

    public string ColumnName { get; }

    public Table.ColumnExpression ResolveColumn(Table table)
    {
        _column = table.ColumnFor(ColumnName);
        return _column == null ? table.AddColumn<int>(ColumnName) : new Table.ColumnExpression(table, _column);
    }

    public string UpdateFieldSql(Table table)
    {
        return $"{ColumnName} = {_value}";
    }

    public bool RequiresInput => false;

    public string ToInsertExpression(Table table)
    {
        return _value.ToString();
    }

    public string ToValueAccessorCode(Variable eventVariable)
    {
        throw new NotSupportedException();
    }
}
