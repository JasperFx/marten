using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventProgressionTable: Table
{
    public const string Name = "mt_event_progression";

    public EventProgressionTable(EventGraph eventGraph): base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        AddColumn<string>("name").AsPrimaryKey();
        AddColumn("last_seq_id", "bigint").AllowNulls();
        AddColumn("last_updated", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");

        if (eventGraph.UseOptimizedProjectionRebuilds)
        {
            AddColumn<string>("mode").DefaultValueByString(ShardMode.none.ToString());
            AddColumn<int>("rebuild_threshold").DefaultValueByExpression("0");
            AddColumn<int>("assigned_node").DefaultValueByExpression("0");
        }

        PrimaryKeyName = "pk_mt_event_progression";
    }
}
