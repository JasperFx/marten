using System;
using Marten.Linq.QueryHandlers;
using Marten.Util;
using Npgsql;

namespace Marten.Events
{
    internal class EventQueryHandler : ListQueryHandler<IEvent>
    {
        private readonly EventSelector _selector;
        private readonly Guid _streamId;
        private readonly DateTime? _timestamp;
        private readonly int? _limit;
        private readonly int _version;

        public EventQueryHandler(EventSelector selector, Guid streamId, int version = 0, DateTime? timestamp = null, int? limit = null)
            : base(selector)
        {
            if (timestamp != null && timestamp.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp), "This method only accepts UTC dates");
            }

            _selector = selector;
            _streamId = streamId;
            _version = version;
            _timestamp = timestamp;
            _limit = limit;
        }

        public override Type SourceType => typeof(IEvent);

        public override void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = _selector.ToSelectClause(null);

            var param = command.AddParameter(_streamId);
            sql += $" where stream_id = :{param.ParameterName}";

            if (_version > 0)
            {
                var versionParam = command.AddParameter(_version);
                sql += " and version <= :" + versionParam.ParameterName;
            }

            if (_timestamp.HasValue)
            {
                var timestampParam = command.AddParameter(_timestamp.Value);
                sql += " and timestamp <= :" + timestampParam.ParameterName;
            }

            sql += " order by version";

            if (_limit.HasValue)
            {
                var limitParam = command.AddParameter(_limit.Value);
                sql += " limit :" + limitParam.ParameterName;
            }

            command.AppendQuery(sql);
        }
    }
}