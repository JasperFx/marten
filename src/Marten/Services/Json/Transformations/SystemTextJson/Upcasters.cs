using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;

namespace Marten.Services.Json.Transformations.SystemTextJson
{
    public abstract class Upcaster<TOldEvent, TEvent>: EventUpcaster<TEvent>
    {
        public override string EventTypeName => (typeof(TOldEvent)).GetEventTypeName();
        public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index) =>
            Transformations.FromDbDataReader<TOldEvent, TEvent>(Upcast)(serializer, dbDataReader, index);

        public override Task<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader,
            int index, CancellationToken ct) =>
            Transformations.FromDbDataReaderAsync<TOldEvent, TEvent>(Upcast)(serializer, dbDataReader, index, ct);

        protected abstract TEvent Upcast(TOldEvent oldEvent);
    }

    public abstract class Upcaster<TEvent>: EventUpcaster<TEvent>
    {
        public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index) =>
            Transformations.FromDbDataReader(Upcast)(serializer, dbDataReader, index);

        protected abstract TEvent Upcast(JsonDocument oldEvent);
    }
}
