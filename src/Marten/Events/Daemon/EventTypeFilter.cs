using System;
using System.Linq;
using Marten.Linq.SqlGeneration;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events.Daemon
{
    internal class EventTypeFilter: ISqlFragment
    {
        private readonly string[] _typeNames;

        public EventTypeFilter(EventGraph graph, Type[] eventTypes)
        {
            EventTypes = eventTypes;
            _typeNames = eventTypes.Select(x => graph.EventMappingFor((Type) x).Alias).ToArray();
        }

        public Type[] EventTypes { get; }


        public void Apply(CommandBuilder builder)
        {
            var parameters = builder.AppendWithParameters("d.type = ANY(?)");
            parameters[0].NpgsqlDbType = NpgsqlDbType.Varchar | NpgsqlDbType.Array;
            parameters[0].Value = _typeNames;
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}
