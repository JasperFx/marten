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

        // #4596 Session 3: progression keying for per-tenant partitioning.
        // No tenant_id column — the per-tenant key lives inside the
        // `name` value itself via ShardName.Identity (jasperfx#407 Phase 0
        // grammar: `{Name}:{ShardKey}:{tenantId}`). Per-tenant daemon shards
        // (Phase 2) get ShardNames with the TenantId slot populated, which
        // produces a distinct Identity per (projection, shardKey, tenant) and
        // therefore a distinct row in this single-PK table — no schema-shape
        // change to mt_event_progression.
        //
        // The high-water-mark shard hardcodes its Identity to the
        // ShardState.HighWaterMark constant (the tenant slot is discarded in
        // jasperfx). Per-tenant high-water tracking, when Phase 2 lands,
        // composes its row name on the Marten side as
        // `$"{ShardState.HighWaterMark}:{tenantId}"` — same single-PK shape.

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
