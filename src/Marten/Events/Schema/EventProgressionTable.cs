using System;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventProgressionTable: Table
{
    public const string Name = "mt_event_progression";

    public EventProgressionTable(EventGraph eventGraph): base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        foreach (var index in eventGraph.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        AddColumn<string>("name").AsPrimaryKey();
        AddColumn("last_seq_id", "bigint").AllowNulls();
        AddColumn("last_updated", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");

        // #4596 Session 1: per-tenant progression key prep. Phase 1 splits
        // `mt_event_progression` rows from `(name)` to `(name, tenant_id)` so each
        // tenant's projection progress is independent. Session 1 only adds the
        // column (nullable, default null = store-global row = today's behavior).
        // Session 3 promotes it into the primary key; until then existing
        // single-row progression continues to work unchanged because every read
        // / write still uses `name` and treats `tenant_id IS NULL` as the global row.
        if (eventGraph.UseTenantPartitionedEvents)
        {
            AddColumn("tenant_id", "varchar").AllowNulls();
        }

        if (eventGraph.UseOptimizedProjectionRebuilds)
        {
            AddColumn<string>("mode").DefaultValueByString(ShardMode.none.ToString());
            AddColumn<int>("rebuild_threshold").DefaultValueByExpression("0");
            AddColumn<int>("assigned_node").DefaultValueByExpression("0");
        }

        if (eventGraph.EnableExtendedProgressionTracking)
        {
            AddColumn("heartbeat", "timestamp with time zone").AllowNulls();
            AddColumn("agent_status", "varchar(20)").AllowNulls();
            AddColumn("pause_reason", "text").AllowNulls();
            AddColumn("running_on_node", "integer").AllowNulls();
            AddColumn("warning_behind_threshold", "bigint").AllowNulls();
            AddColumn("critical_behind_threshold", "bigint").AllowNulls();
        }

        PrimaryKeyName = "pk_mt_event_progression";
    }
}
