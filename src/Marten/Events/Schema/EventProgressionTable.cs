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
        // jasperfx). Per-tenant high-water tracking composes its row name on
        // the Marten side as `$"{ShardState.HighWaterMark}:{tenantId}"` — same
        // single-PK shape. See Marten.Events.Daemon.HighWater.HighWaterShardIdentity
        // for the canonical producer (#4681).

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

            // #5173: warning_behind_threshold / critical_behind_threshold used to be created here.
            // They were created, selected and hydrated onto ShardState -- and written by nothing, in
            // any repo, so every read of them returned NULL. Two columns of storage, two entries in
            // every extended-tracking SELECT and two selector ordinals, carrying nothing. Removed
            // rather than wired: nothing ever owned the value. Existing deployments get an
            // `alter table ... drop column` on their next apply, which is lossless because the
            // columns were provably always NULL.

            // #5048 / jasperfx#565: the classified reason this shard is paused or stopped, so a consumer
            // polling the database (CritterWatch when the publishing node is DOWN, which is exactly when
            // it matters) sees the same reason an in-process ShardState observer does. The reason *text*
            // needs no new column -- ShardFailure.Detail is precisely what pause_reason has always
            // carried. failure_category stores the enum NAME, never the ordinal, so reordering
            // ShardFailureCategory can never silently re-label persisted rows.
            AddColumn("failure_category", "varchar(50)").AllowNulls();
            AddColumn("failure_event_sequence", "bigint").AllowNulls();
            AddColumn("failure_event_type", "varchar(500)").AllowNulls();
            AddColumn("failure_event_tenant_id", "varchar(500)").AllowNulls();
        }

        PrimaryKeyName = "pk_mt_event_progression";
    }
}
