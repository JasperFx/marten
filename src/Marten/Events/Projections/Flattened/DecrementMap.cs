using LamarCodeGeneration.Model;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened
{
    internal class DecrementMap: IColumnMap
    {
        private TableColumn? _column;

        public DecrementMap(string columnName)
        {
            ColumnName = columnName;
        }

        public Table.ColumnExpression ResolveColumn(Table table)
        {
            _column = table.ColumnFor(ColumnName);
            return _column == null ? table.AddColumn<int>(ColumnName) : new Table.ColumnExpression(table, _column);
        }

        public string UpdateFieldSql(Table table) => $"{ColumnName} = {table.Identifier.Name}.{ColumnName} - 1";
        public bool RequiresInput { get; } = false;
        public string ToInsertExpression(Table table) => "0";

        public string ColumnName { get; }

        public string ToValueAccessorCode(Variable eventVariable)
        {
            throw new System.NotSupportedException();
        }
    }
}
