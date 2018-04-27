using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events
{
    internal class StreamStateByGuidHandler : StreamStateByIdHandler<Guid>
    {
        public StreamStateByGuidHandler(EventGraph events, Guid streamId, string tenantId = null) : base(events, streamId, tenantId)
        {
        }
    }

    internal class StreamStateByStringHandler : StreamStateByIdHandler<string>
    {
        public StreamStateByStringHandler(EventGraph events, string streamKey, string tenantId = null) : base(events, streamKey, tenantId)
        {
        }
    }

    internal class StreamStateByIdHandler<T> : IQueryHandler<StreamState>, ISelector<StreamState>
    {
        private readonly T _streamKey;
        private readonly EventGraph _events;
        private readonly string _tenantId;

        public StreamStateByIdHandler(EventGraph events, T streamKey, string tenantId = null)
        {
            if (events.TenancyStyle == TenancyStyle.Conjoined && tenantId == null)
            {
                throw new ArgumentNullException(nameof(tenantId), $"{nameof(tenantId)} cannot be null for {TenancyStyle.Conjoined}");
            }
            _streamKey = streamKey;
            _events = events;
            _tenantId = tenantId;
        }

        public void ConfigureCommand(CommandBuilder sql)
        {
            WriteSelectClause(sql, null);

            var param = sql.AddParameter(_streamKey);
            sql.Append(" where id = :");
            sql.Append(param.ParameterName);

            if (_events.TenancyStyle == TenancyStyle.Conjoined)
            {
                var tenantIdParam = sql.AddParameter(_tenantId);
                sql.Append(" and tenant_id = :");
                sql.Append(tenantIdParam.ParameterName);
            }
        }

        public Type SourceType => typeof(StreamState);

        public StreamState Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return reader.Read() ? Resolve(reader, map, stats) : null;
        }

        public async Task<StreamState> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? await ResolveAsync(reader, map, stats, token).ConfigureAwait(false)
                : null;
        }

        public StreamState Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var id = reader.GetFieldValue<T>(0);
            var version = reader.GetFieldValue<int>(1);
            var typeName = reader.IsDBNull(2) ? null : reader.GetFieldValue<string>(2);
            var timestamp = reader.GetFieldValue<DateTime>(3);
            var created = reader.GetFieldValue<DateTime>(4);

            Type aggregateType = null;
            if (typeName.IsNotEmpty())
            {
                aggregateType = _events.AggregateTypeFor(typeName);
            }

            return StreamState.Create(id, version, aggregateType, timestamp.ToUniversalTime(), created);
        }

        public async Task<StreamState> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<T>(0, token).ConfigureAwait(false);
            var version = await reader.GetFieldValueAsync<int>(1, token).ConfigureAwait(false);
            var typeName = await reader.IsDBNullAsync(2, token).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);
            var timestamp = await reader.GetFieldValueAsync<DateTime>(3, token).ConfigureAwait(false);
            var created = await reader.GetFieldValueAsync<DateTime>(4, token).ConfigureAwait(false);

            Type aggregateType = null;
            if (typeName.IsNotEmpty())
            {
                aggregateType = _events.AggregateTypeFor(typeName);
            }

            return StreamState.Create(id, version, aggregateType, timestamp.ToUniversalTime(), created);
        }

        public string[] SelectFields()
        {
            return new string[] { "id", "version", "type", "timestamp", "created" };
        }

        public void WriteSelectClause(CommandBuilder sql, IQueryableDocument mapping)
        {
            sql.Append("select id, version, type, timestamp, created as timestamp from ");
            sql.Append(_events.DatabaseSchemaName);
            sql.Append(".mt_streams");
        }
    }
}