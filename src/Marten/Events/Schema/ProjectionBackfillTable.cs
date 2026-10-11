using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
///     One row per (shard, backfill). <c>position</c> is how far the backfill has patched, <c>ceiling</c> is
///     where the shard stood when it started, and a non-null <c>completed</c> means the backfill never runs
///     again for this shard.
/// </summary>
internal class ProjectionBackfillTable: Table
{
    public const string Name = "mt_projection_backfills";

    public ProjectionBackfillTable(EventGraph eventGraph): base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        AddColumn<string>("name").AsPrimaryKey();
        AddColumn("position", "bigint").NotNull();
        AddColumn("ceiling", "bigint").NotNull();
        AddColumn("completed", "timestamp with time zone").AllowNulls();
        AddColumn("last_updated", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");
    }
}
