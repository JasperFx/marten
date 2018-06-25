using System;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;

namespace Marten.Events
{
    internal class EventSelector : IEventSelector
    {
        public EventGraph Events { get; }
        private readonly ISerializer _serializer;

        internal EventSelector(EventGraph events, ISerializer serializer)
        {
            Events = events;
            _serializer = serializer;
        }

        public IEvent Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var id = reader.GetGuid(0);
            var eventTypeName = reader.GetString(1);
            var version = reader.GetInt32(2);
        
            var mapping = Events.EventMappingFor(eventTypeName);

            if (mapping == null)
            {
                var dotnetTypeName = reader.GetFieldValue<string>(8);
                if (dotnetTypeName.IsEmpty())
                {
                    throw new UnknownEventTypeException(eventTypeName);
                }

                var type = Events.TypeForDotNetName(dotnetTypeName);
                mapping = Events.EventMappingFor(type);
            }

            var dataJson = reader.GetTextReader(3);
            var data = _serializer.FromJson(mapping.DocumentType, dataJson).As<object>();

            var sequence = reader.GetFieldValue<long>(4);
            var stream = reader.GetFieldValue<Guid>(5);
            var timestamp = reader.GetFieldValue<DateTimeOffset>(6);
            var tenantId = reader.GetFieldValue<string>(7);

            var @event = EventStream.ToEvent(data);
            @event.Version = version;
            @event.Id = id;
            @event.Sequence = sequence;
            @event.StreamId = stream;
            @event.Timestamp = timestamp;
            @event.TenantId = tenantId;


            return @event;
        }

        public async Task<IEvent> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var eventTypeName = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);
            var version = await reader.GetFieldValueAsync<int>(2, token).ConfigureAwait(false);
           
            var mapping = Events.EventMappingFor(eventTypeName);

            if (mapping == null)
            {
                var dotnetTypeName = await reader.GetFieldValueAsync<string>(8, token).ConfigureAwait(false);
                if (dotnetTypeName.IsEmpty())
                {
                    throw new UnknownEventTypeException(eventTypeName);
                }
                Type type;
                try 
                {
                    type = Events.TypeForDotNetName(dotnetTypeName);
                }
                catch (ArgumentNullException)
                {
                    throw new UnknownEventTypeException(dotnetTypeName);
                }
                mapping = Events.EventMappingFor(type);
            }

            var dataJson = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(3).ConfigureAwait(false);
            var data = _serializer.FromJson(mapping.DocumentType, dataJson).As<object>();

            var sequence = await reader.GetFieldValueAsync<long>(4, token).ConfigureAwait(false);
            var stream = await reader.GetFieldValueAsync<Guid>(5, token).ConfigureAwait(false);
            var timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(6, token).ConfigureAwait(false);
            var tenantId = await reader.GetFieldValueAsync<string>(7, token).ConfigureAwait(false);

            var @event = EventStream.ToEvent(data);
            @event.Version = version;
            @event.Id = id;
            @event.Sequence = sequence;
            @event.StreamId = stream;
            @event.Timestamp = timestamp;
            @event.TenantId = tenantId;

            return @event;
        }

        public string[] SelectFields()
        {
            return new[] {"id", "type", "version", "data", "seq_id", "stream_id", "timestamp", TenantIdColumn.Name, DocumentMapping.DotNetTypeColumn};
        }

        public void WriteSelectClause(CommandBuilder sql, IQueryableDocument mapping)
        {
            sql.Append($"select id, type, version, data, seq_id, stream_id, timestamp, tenant_id, {DocumentMapping.DotNetTypeColumn} from ");
            sql.Append(Events.DatabaseSchemaName);
            sql.Append(".mt_events as d");
        }
    }
}