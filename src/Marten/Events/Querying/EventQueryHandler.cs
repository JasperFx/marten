using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events.Querying
{
    internal interface IEventQueryHandler: IQueryHandler<IReadOnlyList<IEvent>>
    {
    }

    [Obsolete("Consider replacing this with delegations to Linq handling")]
    internal class EventQueryHandler<TIdentity>: IEventQueryHandler
    {
        private readonly IEventStorage _selector;
        private readonly TIdentity _streamId;
        private readonly DateTime? _timestamp;
        private readonly long _version;
        private readonly TenancyStyle _tenancyStyle;
        private readonly string _tenantId;

        public EventQueryHandler(IEventStorage selector, TIdentity streamId, long version = 0, DateTime? timestamp = null, TenancyStyle tenancyStyle = TenancyStyle.Single, string tenantId = null)
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

        public void ConfigureCommand(CommandBuilder sql, IMartenSession session)
        {
            _selector.WriteSelectClause(sql);

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

        public IReadOnlyList<IEvent> Handle(DbDataReader reader, IMartenSession session)
        {
            var list = new List<IEvent>();
            while (reader.Read())
            {
                var @event = _selector.Resolve(reader);
                list.Add(@event);
            }

            return list;
        }

        public async Task<IReadOnlyList<IEvent>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var list = new List<IEvent>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var @event = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);
                list.Add(@event);
            }

            return list;
        }


    }
}
