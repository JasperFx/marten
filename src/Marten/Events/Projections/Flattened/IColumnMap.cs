using JasperFx.CodeGeneration.Model;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal interface IColumnMap
{
    string ColumnName { get; }

    bool RequiresInput { get; }
    Table.ColumnExpression ResolveColumn(Table table);

    string UpdateFieldSql(Table table);
    string ToInsertExpression(Table table);

    string ToValueAccessorCode(Variable eventVariable);
}
