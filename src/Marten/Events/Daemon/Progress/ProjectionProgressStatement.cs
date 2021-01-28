using Baseline;
using Marten.Linq.SqlGeneration;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events.Daemon.Progress
{
    public class ProjectionProgressStatement: Statement
    {
        private readonly EventGraph _events;

        public ProjectionProgressStatement(EventGraph events) : base(null)
        {
            _events = events;
        }

        public ShardName Name { get; set; }

        protected override void configure(CommandBuilder builder)
        {
            builder.Append($"select name, last_seq_id from {_events.DatabaseSchemaName}.mt_event_progression");
            if (Name != null)
            {
                builder.Append(" where name = :");
                var parameter = builder.AddParameter(Name.Identity, NpgsqlDbType.Varchar);
                builder.Append(parameter.ParameterName);
            }
        }
    }
}
