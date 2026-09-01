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

        // #5309: the optional blocks below are DELIBERATELY not gated on the options that decide
        // whether Marten reads and writes them. The shape of this table is the same for every store
        // regardless of EnableExtendedProgressionTracking and UseOptimizedProjectionRebuilds; only
        // the daemon's use of the columns is conditional.
        //
        // Gating the DDL made two DocumentStores over one DatabaseSchemaName undo each other. The
        // store with the flag off does not merely decline to add the columns -- its expected table
        // genuinely lacks them, the delta resolves them as extras, and Weasel emits `alter table ...
        // drop column` for each. Whichever store applies schema last decides the shape, and they keep
        // stripping each other for as long as both run. Nothing warns either side: the flag-off store
        // logs no reason to, and the flag-on store then fails every progression read with `column
        // "heartbeat" does not exist`. Two processes over one schema -- a service beside a seeder, a
        // reporting job or a migration tool -- is ordinary, and neither of them is doing anything
        // wrong. Found downstream as CritterWatch#1183, where the console went silently dark rather
        // than noisily broken because the poller catches per-database.
        //
        // The columns cost nothing to carry: every one of them is nullable or defaulted, and a store
        // that does not use them never writes them. That is the same trade #5173 already made below
        // for warning/critical_behind_threshold, generalised -- the cost of a conditional column is
        // never the column, it is the schema CHANGE, and a shape that depends on configuration means
        // some pair of stores will disagree about it.
        AddColumn<string>("mode").DefaultValueByString(ShardMode.none.ToString());
        AddColumn<int>("rebuild_threshold").DefaultValueByExpression("0");
        AddColumn<int>("assigned_node").DefaultValueByExpression("0");

        AddColumn("heartbeat", "timestamp with time zone").AllowNulls();
        AddColumn("agent_status", "varchar(20)").AllowNulls();
        AddColumn("pause_reason", "text").AllowNulls();
        AddColumn("running_on_node", "integer").AllowNulls();

        // #5173: warning_behind_threshold / critical_behind_threshold are DELIBERATELY still
        // created, and are the one part of the extended block that is neither written nor read.
        // Nothing in any repo ever owned the value, so #5184 removed them outright -- from the
        // DDL, the SELECT, and the ShardStateSelector ordinals. The read-side half of that stands;
        // only the DDL is restored here.
        //
        // Restored because the cost of removing them is not the two columns, it is the schema
        // CHANGE. Dropping a column from an existing deployment means `alter table ... drop
        // column` on the next apply, and in PostgreSQL that needs ACCESS EXCLUSIVE on a small,
        // hot table that every running daemon writes. The operation itself is O(1) metadata, but
        // the lock queues behind in-flight progression writes and blocks every reader and writer
        // behind it while it waits. Two always-NULL columns are cheaper than asking every
        // deployment to take that lock, so an upgrade stays a no-op on this table.
        //
        // They are not selected and not hydrated -- see ProjectionProgressStatement and
        // ShardStateSelector, whose ordinals now skip them. If they are ever removed for real,
        // it has to be a documented migration step, not a silent auto-apply.
        AddColumn("warning_behind_threshold", "bigint").AllowNulls();
        AddColumn("critical_behind_threshold", "bigint").AllowNulls();

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

        PrimaryKeyName = "pk_mt_event_progression";
    }
}
