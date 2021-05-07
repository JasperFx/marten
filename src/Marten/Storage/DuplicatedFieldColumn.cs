using Marten.Linq.Fields;
using Weasel.Postgresql.Tables;

namespace Marten.Storage
{
    internal class DuplicatedFieldColumn: TableColumn
    {
        private readonly DuplicatedField _field;
        private const string NullConstraint = "NULL";
        private const string NotNullConstraint = "NOT NULL";


        public DuplicatedFieldColumn(DuplicatedField field) : base(field.ColumnName, field.PgType)
        {
            AllowNulls = !field.NotNull;

            _field = field;
        }

        public override string AddColumnSql(Table table)
        {
            return $"{base.AddColumnSql(table)}update {table.Identifier} set {_field.UpdateSqlFragment()};";
        }
    }
}
