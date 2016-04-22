using System;
using System.Data.Common;
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

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = ToSelectClause(null);
            var param = command.AddParameter(_streamId);
            sql += " where id = :" + param.ParameterName;

            command.AppendQuery(sql);
        }

        public Type SourceType => typeof (StreamState);

        public StreamState Handle(DbDataReader reader, IIdentityMap map)
        {
            return reader.Read() ? Resolve(reader, map) : null;
        }

        public async Task<StreamState> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return await reader.ReadAsync(token) 
                ? await ResolveAsync(reader, map, token) 
                : null;
        }

        public StreamState Resolve(DbDataReader reader, IIdentityMap map)
        {
            var id = reader.GetFieldValue<Guid>(0);
            var version = reader.GetFieldValue<int>(1);
            var typeName = reader.GetFieldValue<string>(2);

            Type aggregateType = null;
            if (typeName.IsNotEmpty())
            {
                aggregateType = _events.AggregateTypeFor(typeName);
            }

            return new StreamState(id, version, aggregateType);
        }

        public async Task<StreamState> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var version = await reader.GetFieldValueAsync<int>(1, token).ConfigureAwait(false);
            var typeName = await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false);

            Type aggregateType = null;
            if (typeName.IsNotEmpty())
            {
                aggregateType = _events.AggregateTypeFor(typeName);
            }

            return new StreamState(id, version, aggregateType);
        }

        public string[] SelectFields()
        {
            return new string[] { "id", "version", "type" };
        }

        public string ToSelectClause(IDocumentMapping mapping)
        {
            return $"select id, version, type from {_events.DatabaseSchemaName}.mt_streams";
        }
    }
}