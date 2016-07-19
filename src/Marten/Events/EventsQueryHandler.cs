using System;
using Marten.Linq.QueryHandlers;
using Marten.Util;
using Npgsql;

namespace Marten.Events
{
    internal class EventsQueryHandler : ListQueryHandler<IEvent>
    {
        private readonly DateTime? _before;
        private readonly DateTime? _after;
        private readonly int _version;

        public EventsQueryHandler(EventSelector selector, DateTime? before = null, DateTime? after = null, int version = 0) 
            : base(selector)
        {
            if (before != null && before.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentOutOfRangeException(nameof(before), "This method only accepts UTC dates");
            }

            if (after != null && after.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentOutOfRangeException(nameof(after), "This method only accepts UTC dates");
            }

            _before = before;
            _after = after;
            _version = version;
        }

        public override Type SourceType => typeof(IEvent);

        public override void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = Selector.ToSelectClause(null);
            sql += $" where 1 = 1";

            if (_version > 0)
            {
                var versionParam = command.AddParameter(_version);
                sql += " and version <= :" + versionParam.ParameterName;
            }
            
            if (_before.HasValue)
            {
                var beforeParam = command.AddParameter(_before.Value);
                sql += " and timestamp <= :" + beforeParam.ParameterName;
            }

            if (_after.HasValue)
            {
                var afterParam = command.AddParameter(_after.Value);
                sql += " and timestamp >= :" + afterParam.ParameterName;
            }

            sql += " order by version";

            command.AppendQuery(sql);
        }        
    }
}
