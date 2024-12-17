using System.Linq;
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

    protected override void configure(ICommandBuilder builder)
    {
        if (_events.UseOptimizedProjectionRebuilds)
        {
            builder.Append($"select name, last_seq_id, mode, rebuild_threshold, assigned_node from {_events.DatabaseSchemaName}.mt_event_progression");
        }
        else
        {
            builder.Append($"select name, last_seq_id from {_events.DatabaseSchemaName}.mt_event_progression");
        }


        if (Name != null)
        {
            builder.Append(" where name = ");
            builder.AppendParameter(Name.Identity);
        }

        if (Names != null)
        {
            builder.Append(" where name = ANY(");
            builder.AppendParameter(Names.Select(x => x.Identity).ToArray());
            builder.Append(")");
        }
    }
}
