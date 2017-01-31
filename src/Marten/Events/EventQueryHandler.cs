using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Events
{
    internal class EventQueryHandler : IQueryHandler<IList<IEvent>>
    {
        private readonly EventSelector _selector;
        private readonly Guid _streamId;
        private readonly DateTime? _timestamp;
        private readonly int _version;

        public EventQueryHandler(EventSelector selector, Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            if (timestamp != null && timestamp.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp), "This method only accepts UTC dates");
            }

            _selector = selector;
            _streamId = streamId;
            _version = version;
            _timestamp = timestamp;
        }

        public Type SourceType => typeof(IEvent);

        public void ConfigureCommand(NpgsqlCommand command)
        {
            // TODO -- pull from StringBuilder pool

            var sql = new StringBuilder();
            sql.Append(_selector.ToSelectClause(null));

 
            var param = command.AddParameter(_streamId);
            sql.Append(" where stream_id = :");
            sql.Append(param.ParameterName);

            if (_version > 0)
            {
                var versionParam = command.AddParameter(_version);
                sql.Append(" and version <= :");
                sql.Append(versionParam.ParameterName);
            }

            if (_timestamp.HasValue)
            {
                var timestampParam = command.AddParameter(_timestamp.Value);
                sql.Append(" and timestamp <= :");
                sql.Append(timestampParam.ParameterName);
            }

            sql.Append(" order by version");

            command.AppendQuery(sql.ToString());
        }

        public IList<IEvent> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _selector.Read(reader, map, stats);
        }

        public Task<IList<IEvent>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return _selector.ReadAsync(reader, map, stats, token);
        }

    }
}