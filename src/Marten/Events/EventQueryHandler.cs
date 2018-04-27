using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events
{
    internal interface IEventQueryHandler : IQueryHandler<IReadOnlyList<IEvent>>
    {
    }

    internal class EventQueryHandler<TIdentity> : IEventQueryHandler
    {
        private readonly ISelector<IEvent> _selector;
        private readonly TIdentity _streamId;
        private readonly DateTime? _timestamp;
        private readonly int _version;
        private readonly TenancyStyle _tenancyStyle;
        private readonly string _tenantId;

        public EventQueryHandler(ISelector<IEvent> selector, TIdentity streamId, int version = 0, DateTime? timestamp = null, TenancyStyle tenancyStyle = TenancyStyle.Single, string tenantId = null)
        {
            if (timestamp != null && timestamp.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp), "This method only accepts UTC dates");
            }

            if (_tenancyStyle == TenancyStyle.Conjoined && tenantId == null)
            {
                throw new ArgumentNullException(nameof(tenantId), $"{nameof(tenantId)} cannot be null for {TenancyStyle.Conjoined}");
            }

            _selector = selector;
            _streamId = streamId;
            _version = version;
            _timestamp = timestamp;
            _tenancyStyle = tenancyStyle;
            _tenantId = tenantId;
        }

        public Type SourceType => typeof(IEvent);

        public void ConfigureCommand(CommandBuilder sql)
        {
            _selector.WriteSelectClause(sql, null);

            var param = sql.AddParameter(_streamId);
            sql.Append(" where stream_id = :");
            sql.Append(param.ParameterName);

            if (_version > 0)
            {
                var versionParam = sql.AddParameter(_version);
                sql.Append(" and version <= :");
                sql.Append(versionParam.ParameterName);
            }

            if (_timestamp.HasValue)
            {
                var timestampParam = sql.AddParameter(_timestamp.Value);
                sql.Append(" and timestamp <= :");
                sql.Append(timestampParam.ParameterName);
            }

            if (_tenancyStyle == TenancyStyle.Conjoined)
            {
                var tenantIdParam = sql.AddParameter(_tenantId);
                sql.Append(" and tenant_id = :");
                sql.Append(tenantIdParam.ParameterName);
            }

            sql.Append(" order by version");
        }

        public IReadOnlyList<IEvent> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _selector.Read(reader, map, stats);
        }

        public Task<IReadOnlyList<IEvent>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return _selector.ReadAsync(reader, map, stats, token);
        }
    }
}