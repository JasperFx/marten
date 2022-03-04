using System;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Filter on a single aggregate type
    /// </summary>
    internal class AggregateTypeFilter : ISqlFragment
    {
        public AggregateTypeFilter(Type aggregateType, EventGraph events)
        {
            AggregateType = aggregateType;
            Alias = events.AggregateAliasFor(aggregateType);
        }

        public Type AggregateType { get; }

        public string Alias { get; }

        public void Apply(CommandBuilder builder)
        {
            var parameter = builder.AddParameter(Alias, NpgsqlDbType.Varchar);
            builder.Append("s.type = :");
            builder.Append(parameter.ParameterName);
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}
