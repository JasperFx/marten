using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.Json.Transformations
{
    public interface IEventUpcaster
    {
        string EventTypeName { get; }
        Type EventType { get; }

        object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index);
        Task<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader, int index, CancellationToken ct);
    }

    public abstract class EventUpcaster: IEventUpcaster
    {
        public abstract string EventTypeName { get; }
        public abstract Type EventType { get; }

        public abstract object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index);

        public virtual Task<object>
            FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader, int index, CancellationToken ct) =>
            Task.FromResult(FromDbDataReader(serializer, dbDataReader, index));
    }

    public abstract class EventUpcaster<TEvent>: EventUpcaster
    {
        public override Type EventType => typeof(TEvent);
    }
}
