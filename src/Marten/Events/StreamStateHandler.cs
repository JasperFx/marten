using System;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Events
{
    internal class StreamStateHandler : IQueryHandler<StreamState>, ISelector<StreamState>
    {
        private readonly Guid _streamId;
        private readonly EventGraph _events;

        public StreamStateHandler(EventGraph events, Guid streamId)
        {
            _streamId = streamId;

            _events = events;
        }

        public void ConfigureCommand(CommandBuilder sql)
        {
            WriteSelectClause(sql, null);


            var param = sql.AddParameter(_streamId);
            sql.Append(" where id = :");
            sql.Append(param.ParameterName);
        }

        public Type SourceType => typeof (StreamState);

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
            var id = reader.GetFieldValue<Guid>(0);
            var version = reader.GetFieldValue<int>(1);
            var typeName = reader.IsDBNull(2) ? null : reader.GetFieldValue<string>(2);
            var timestamp = reader.GetFieldValue<DateTime>(3);

            Type aggregateType = null;
            if (typeName.IsNotEmpty())
            {
                aggregateType = _events.AggregateTypeFor(typeName);
            }

            return new StreamState(id, version, aggregateType, timestamp.ToUniversalTime());
        }

        public async Task<StreamState> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var version = await reader.GetFieldValueAsync<int>(1, token).ConfigureAwait(false);
            var typeName = await reader.IsDBNullAsync(2, token).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);
            var timestamp = await reader.GetFieldValueAsync<DateTime>(3, token).ConfigureAwait(false);

            Type aggregateType = null;
            if (typeName.IsNotEmpty())
            {
                aggregateType = _events.AggregateTypeFor(typeName);
            }

            return new StreamState(id, version, aggregateType, timestamp.ToUniversalTime());
        }

        public string[] SelectFields()
        {
            return new string[] { "id", "version", "type", "timestamp" };
        }

        public void WriteSelectClause(CommandBuilder sql, IQueryableDocument mapping)
        {
            sql.Append("select id, version, type, timestamp as timestamp from ");
            sql.Append(_events.DatabaseSchemaName);
            sql.Append(".mt_streams");
        }
    }
}