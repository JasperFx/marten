using Marten.Linq.Members;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

internal class DuplicatedFieldColumn: TableColumn
{
    private const string NullConstraint = "NULL";
    private const string NotNullConstraint = "NOT NULL";
    private readonly DuplicatedField _field;


    public DuplicatedFieldColumn(DuplicatedField field): base(field.ColumnName, field.PgType)
    {
        AllowNulls = !field.NotNull;

        _field = field;
    }

    public override string AddColumnSql(Table table)
    {
        return $"{base.AddColumnSql(table)}update {table.Identifier} set {_field.UpdateSqlFragment()};";
    }

    public override string AlterColumnTypeSql(Table table, TableColumn changeActual)
    {
        return $"alter table {table.Identifier} drop column {Name};{AddColumnSql(table)}";
    }
}
