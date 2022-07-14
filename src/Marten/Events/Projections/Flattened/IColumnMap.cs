using LamarCodeGeneration.Model;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened
{
    internal interface IColumnMap
    {
        Table.ColumnExpression ResolveColumn(Table table);

        string ColumnName { get; }

        string UpdateFieldSql(Table table);

        bool RequiresInput { get; }
        string ToInsertExpression(Table table);

        string ToValueAccessorCode(Variable eventVariable);
    }
}
