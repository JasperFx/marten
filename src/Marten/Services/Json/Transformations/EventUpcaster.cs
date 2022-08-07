#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Exceptions;

namespace Marten.Services.Json.Transformations
{
    public interface IEventUpcaster
    {
        string EventTypeName { get; }
        Type EventType { get; }

        object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index);

        ValueTask<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader, int index,
            CancellationToken ct);
    }

    public abstract class EventUpcaster: IEventUpcaster
    {
        public abstract string EventTypeName { get; }
        public abstract Type EventType { get; }

        public abstract object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index);

        public virtual ValueTask<object>
            FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader, int index, CancellationToken ct) =>
            new(FromDbDataReader(serializer, dbDataReader, index));
    }

    public abstract class EventUpcaster<TEvent>: EventUpcaster
    {
        public override Type EventType => typeof(TEvent);
    }

    public abstract class EventUpcaster<TOldEvent, TEvent>: EventUpcaster<TEvent>
        where TOldEvent : notnull where TEvent : notnull
    {
        public override string EventTypeName => (typeof(TOldEvent)).GetEventTypeName();

        public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index) =>
            JsonTransformations.FromDbDataReader<TOldEvent, TEvent>(Upcast)(serializer, dbDataReader, index);

        public override ValueTask<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader,
            int index, CancellationToken ct) =>
            JsonTransformations.FromDbDataReaderAsync<TOldEvent, TEvent>(Upcast)(
                serializer, dbDataReader, index, ct
            );

        protected abstract TEvent Upcast(TOldEvent oldEvent);
    }

    public abstract class AsyncOnlyEventUpcaster<TOldEvent, TEvent>: EventUpcaster<TEvent>
        where TOldEvent : notnull where TEvent : notnull
    {
        public override string EventTypeName => (typeof(TOldEvent)).GetEventTypeName();

        public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index) =>
            throw new MartenException(
                $"Cannot use AsyncOnlyEventUpcaster of type {GetType().AssemblyQualifiedName} in the synchronous API.");

        public override ValueTask<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader,
            int index, CancellationToken ct) =>
            JsonTransformations.FromDbDataReaderAsync<TOldEvent, TEvent>(UpcastAsync)(
                serializer, dbDataReader, index, ct
            );

        protected abstract Task<TEvent> UpcastAsync(TOldEvent oldEvent, CancellationToken ct);
    }
}
