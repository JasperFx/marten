using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;

namespace Marten.Events
{
    internal class EventSelector : ISelector<IEvent>
    {
        private readonly EventGraph _events;
        private readonly ISerializer _serializer;

        internal EventSelector(EventGraph events, ISerializer serializer)
        {
            _events = events;
            _serializer = serializer;
        }

        public IEvent Resolve(DbDataReader reader, IIdentityMap map)
        {
            var id = reader.GetGuid(0);
            var eventTypeName = reader.GetString(1);
            var version = reader.GetInt32(2);
            var dataJson = reader.GetString(3);

            var mapping = _events.EventMappingFor(eventTypeName);

            var data = _serializer.FromJson(mapping.DocumentType, dataJson).As<object>();


            var @event = EventStream.ToEvent(data);
            @event.Version = version;
            @event.Id = id;

            return @event;
        }

        public async Task<IEvent> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var id = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
            var eventTypeName = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);
            var version = await reader.GetFieldValueAsync<int>(2, token).ConfigureAwait(false);
            var dataJson = await reader.GetFieldValueAsync<string>(3, token).ConfigureAwait(false);

            var mapping = _events.EventMappingFor(eventTypeName);

            var data = _serializer.FromJson(mapping.DocumentType, dataJson).As<object>();


            var @event = EventStream.ToEvent(data);
            @event.Version = version;
            @event.Id = id;

            return @event;
        }

        public string[] SelectFields()
        {
            return new[] {"id", "type", "version", "data"};
        }

        public string ToSelectClause(IDocumentMapping mapping)
        {
            return $"select id, type, version, data from {_events.DatabaseSchemaName}.mt_events";
        }
    }
}