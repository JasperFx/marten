using Marten.Linq.Fields;

namespace Marten.Storage
{
    internal class DuplicatedFieldColumn: TableColumn
    {
        private readonly DuplicatedField _field;
        private const string NullConstraint = "NULL";
        private const string NotNullConstraint = "NOT NULL";


        public DuplicatedFieldColumn(DuplicatedField field) : base(field.ColumnName, field.PgType, field.NotNull ? NotNullConstraint : NullConstraint)
        {
            CanAdd = true;
            _field = field;
        }

        public override string AddColumnSql(Table table)
        {
            return $"{base.AddColumnSql(table)}update {table.Identifier} set {_field.UpdateSqlFragment()};";
        }
    }
}
