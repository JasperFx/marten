using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JasperFx.Core;
using Weasel.Core;
using Weasel.Postgresql.Functions;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

/// <summary>
///     Recipe for creating a simple upsert function based on a table structure
/// </summary>
internal class FlatTableUpsertFunction: Function
{
    private readonly List<IColumnMap> _columns;
    private readonly DbObjectName _identifier;
    private readonly Table _table;

    public FlatTableUpsertFunction(DbObjectName identifier, Table table, List<IColumnMap> columns): base(identifier)
    {
        _identifier = identifier;
        _table = table;
        _columns = columns;
    }

    /// <summary>
    ///     True when this event maps only a subset of the table's non-primary-key
    ///     columns. Partial events generate UPDATE-only functions so that they cannot
    ///     violate NOT NULL constraints on columns they don't populate (#4255).
    /// </summary>
    internal bool IsPartialMapping
    {
        get
        {
            var mappedColumnNames = _columns.Select(x => x.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var pkColumnNames = _table.PrimaryKeyColumns
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return _table.Columns
                .Where(c => !pkColumnNames.Contains(c.Name))
                .Any(c => !mappedColumnNames.Contains(c.Name));
        }
    }

    public override void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        var pkColumns = _table.PrimaryKeyColumns.Select(x => _table.ColumnFor(x)).ToArray();

        // Arguments
        var argList = arguments(pkColumns).Join(", ");

        if (IsPartialMapping)
        {
            // For partial-mapping events, only UPDATE the existing row. If no row exists,
            // this is a no-op — which is safer than inserting a partially populated row
            // that may violate NOT NULL constraints on unmapped columns (#4255).
            var updates = _columns.Select(x => x.UpdateFieldSql(_table)).Join(", ");
            var whereClause = _table.PrimaryKeyColumns
                .Select(c => $"{c} = {_table.ColumnFor(c).ToArgumentName()}")
                .Join(" AND ");

            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS void LANGUAGE plpgsql
AS $function$
BEGIN
UPDATE {_table.Identifier.QualifiedName} SET {updates}
  WHERE {whereClause};
END;
$function$;
");
            return;
        }

        var inserts = _table.PrimaryKeyColumns.Concat(_columns.Select(x => x.ColumnName)).Join(", ");

        // Insert values
        var insertExpressions = insertValues(pkColumns).Join(", ");

        var allUpdates = _columns.Select(x => x.UpdateFieldSql(_table)).Join(", ");

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS void LANGUAGE plpgsql
AS $function$
BEGIN
INSERT INTO {_table.Identifier.QualifiedName} ({inserts}) VALUES ({insertExpressions})
  ON CONFLICT ON CONSTRAINT {_table.PrimaryKeyName}
  DO UPDATE SET {allUpdates};
END;
$function$;
");
    }

    private IEnumerable<string> insertValues(TableColumn?[] pkColumns)
    {
        foreach (var pkColumn in pkColumns) yield return pkColumn.ToArgumentName();

        foreach (var column in _columns) yield return column.ToInsertExpression(_table);
    }

    private IEnumerable<string> arguments(TableColumn?[] pkColumns)
    {
        foreach (var column in pkColumns) yield return column.ToFunctionArgumentDeclaration();

        var inputColumns = _columns.Where(x => x.RequiresInput);
        foreach (var inputColumn in inputColumns)
        {
            var tableColumn = _table.ColumnFor(inputColumn.ColumnName);
            yield return tableColumn.ToFunctionArgumentDeclaration();
        }
    }
}
