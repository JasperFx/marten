using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Weasel.Core;
using Weasel.Postgresql.Functions;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened
{
    /// <summary>
    /// Recipe for creating a simple upsert function based on a table structure
    /// </summary>
    internal class FlatTableUpsertFunction : Function
    {
        private readonly DbObjectName _identifier;
        private readonly Table _table;
        private readonly List<IColumnMap> _columns;

        public FlatTableUpsertFunction(DbObjectName identifier, Table table, List<IColumnMap> columns) : base(identifier)
        {
            _identifier = identifier;
            _table = table;
            _columns = columns;
        }

        public override void WriteCreateStatement(Migrator migrator, TextWriter writer)
        {
            var pkColumns = _table.PrimaryKeyColumns.Select(x => _table.ColumnFor(x)).ToArray();

            var inserts = _table.PrimaryKeyColumns.Concat(_columns.Select(x => x.ColumnName)).Join(", ");

            // Arguments
            var argList = arguments(pkColumns).Join(", ");

            // Insert values
            var insertExpressions = insertValues(pkColumns).Join(", ");

            var updates = _columns.Select(x => x.UpdateFieldSql(_table)).Join(", ");

            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS void LANGUAGE plpgsql
AS $function$
BEGIN
INSERT INTO {_table.Identifier.QualifiedName} ({inserts}) VALUES ({insertExpressions})
  ON CONFLICT ON CONSTRAINT {_table.PrimaryKeyName}
  DO UPDATE SET {updates};
END;
$function$;
");
        }

        private IEnumerable<string> insertValues(TableColumn?[] pkColumns)
        {
            foreach (var pkColumn in pkColumns)
            {
                yield return pkColumn.ToArgumentName();
            }

            foreach (var column in _columns)
            {
                yield return column.ToInsertExpression(_table);
            }
        }

        private IEnumerable<string> arguments(TableColumn?[] pkColumns)
        {

            foreach (var column in pkColumns)
            {
                yield return column.ToFunctionArgumentDeclaration();
            }

            var inputColumns = _columns.Where(x => x.RequiresInput);
            foreach (var inputColumn in inputColumns)
            {
                var tableColumn = _table.ColumnFor(inputColumn.ColumnName);
                yield return tableColumn.ToFunctionArgumentDeclaration();
            }
        }
    }
}
