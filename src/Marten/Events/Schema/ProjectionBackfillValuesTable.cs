using Marten.Schema;
using Marten.Storage;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
///     The backfilled members of one document type, per (backfill, tenant, document), until they are handed
///     over. The live projection never writes this table, so it cannot overwrite what the backfill folds.
///     <c>merged_xmin</c> is the document row's version after its last merge: a different version means the
///     live projection has rewritten the row since, and the hand-over merges it again.
/// </summary>
internal class ProjectionBackfillValuesTable: Table
{
    public ProjectionBackfillValuesTable(DocumentMapping mapping): base(NameFor(mapping))
    {
        AddColumn<string>("name").AsPrimaryKey();
        AddColumn<string>("tenant_id").AsPrimaryKey();
        AddColumn("id", new IdColumn(mapping).Type).AsPrimaryKey();
        AddColumn("data", "jsonb").NotNull();
        AddColumn<bool>("dirty").NotNull().DefaultValueByExpression("true");
        AddColumn("merged_xmin", "xid").AllowNulls();
    }

    public static PostgresqlObjectName NameFor(DocumentMapping mapping) =>
        new(mapping.TableName.Schema, mapping.TableName.Name + "_backfill");
}
