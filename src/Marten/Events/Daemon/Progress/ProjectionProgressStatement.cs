using System.Linq;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.HighWater;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Progress;

internal class ProjectionProgressStatement: Statement
{
    private readonly EventGraph _events;

    public ProjectionProgressStatement(EventGraph events)
    {
        _events = events;
    }

    /// <summary>
    /// If set, filter the projection results to just this shard
    /// </summary>
    public ShardName Name { get; set; }


    /// <summary>
    /// If set, filter the projection results to these shard names
    /// </summary>
    public ShardName[]? Names { get; set; }

    /// <summary>
    /// #4596 Phase 1 Session 4: scope progression rows to a single tenant when the
    /// per-tenant flag is on. Matches rows whose <c>name</c> ends in <c>:tenantId</c>
    /// — that's the 3-segment <see cref="ShardName.Identity"/> grammar
    /// (<c>{Name}:{ShardKey}:{tenantId}</c>) emitted by <see cref="ShardName.Compose"/>
    /// when its tenant slot is populated. Null means "no tenant filter"
    /// (today's behavior — every row).
    /// </summary>
    public string? TenantId { get; set; }

    protected override void configure(ICommandBuilder builder)
    {
        // #5048 / jasperfx#565: the failure_* columns trail the existing extended block so the ordinals
        // ShardStateSelector walks stay stable.
        const string extendedColumns =
            "heartbeat, agent_status, pause_reason, running_on_node, warning_behind_threshold, critical_behind_threshold, failure_category, failure_event_sequence, failure_event_type, failure_event_tenant_id";

        if (_events.UseOptimizedProjectionRebuilds && _events.EnableExtendedProgressionTracking)
        {
            builder.Append($"select name, last_seq_id, mode, rebuild_threshold, assigned_node, {extendedColumns} from {_events.DatabaseSchemaName}.mt_event_progression");
        }
        else if (_events.UseOptimizedProjectionRebuilds)
        {
            builder.Append($"select name, last_seq_id, mode, rebuild_threshold, assigned_node from {_events.DatabaseSchemaName}.mt_event_progression");
        }
        else if (_events.EnableExtendedProgressionTracking)
        {
            builder.Append($"select name, last_seq_id, {extendedColumns} from {_events.DatabaseSchemaName}.mt_event_progression");
        }
        else
        {
            builder.Append($"select name, last_seq_id from {_events.DatabaseSchemaName}.mt_event_progression");
        }


        var whereStarted = false;

        if (Name != null)
        {
            builder.Append(" where name = ");
            builder.AppendParameter(Name.Identity);
            whereStarted = true;
        }

        if (Names != null)
        {
            builder.Append(whereStarted ? " and " : " where ");
            builder.Append("name = ANY(");
            builder.AppendParameter(Names.Select(x => x.Identity).ToArray());
            builder.Append(")");
            whereStarted = true;
        }

        if (TenantId != null)
        {
            builder.Append(whereStarted ? " and " : " where ");
            // Tenant-bearing ShardName.Identity always ends in `:{tenantId}`.
            // Match the trailing tenant suffix via LIKE — partition suffixes
            // are valid PG identifiers so they don't contain LIKE wildcards.
            builder.Append("name like ");
            builder.AppendParameter("%:" + TenantId);
            whereStarted = true;
        }

        // #5108: the allocation-fence row is high-water bookkeeping, not a projection shard, and must
        // never be reported as one — see HighWaterAllocationFence.
        builder.Append(whereStarted ? " and " : " where ");
        builder.Append("name <> ");
        builder.AppendParameter(HighWaterAllocationFence.ProgressionName);
    }
}
