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

            // #5171: a tenant-bearing ShardName.Identity always ends in `:{tenantId}`, but this used to
            // be matched with `name like '%:' || tenantId`, and `_` is BOTH a legal tenant-id character
            // and a LIKE single-character wildcard — so tenant `acme_corp` also swept in `acmeXcorp`'s
            // rows. Compare the literal trailing substring instead: no pattern grammar, no escaping to
            // get wrong, and index-neutral (the old leading-`%` LIKE could never use an index either).
            var suffix = ":" + TenantId;
            builder.Append("right(name, char_length(");
            builder.AppendParameter(suffix);
            builder.Append(")) = ");
            builder.AppendParameter(suffix);
            whereStarted = true;
        }

        // #5108/#5109: high-water bookkeeping rows are not projection shards and must never be
        // reported as such — see HighWaterAllocationFence and HighWaterStuckGap.
        builder.Append(whereStarted ? " and " : " where ");
        builder.Append("name <> ALL(");
        builder.AppendParameter(new[]
        {
            HighWaterAllocationFence.ProgressionName, HighWaterStuckGap.ProgressionName
        });
        builder.Append(")");
    }
}
