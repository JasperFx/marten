#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.Json.Transformations
{
    public class JsonTransformation
    {
        public Func<ISerializer, DbDataReader, int, object> FromDbDataReader { get; }
        public Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>> FromDbDataReaderAsync { get; }

        public JsonTransformation(
            Func<ISerializer, DbDataReader, int, object> fromDbDataReader,
            Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>>? transformDbDataReaderAsync = null
        )
        {
            FromDbDataReader = fromDbDataReader;
            FromDbDataReaderAsync =
                transformDbDataReaderAsync ?? ((serializer, reader, index, _) =>
                    Task.FromResult(FromDbDataReader(serializer, reader, index)));
        }
    }

    public static class Transformations
    {
        public static JsonTransformation Upcast<TOldEvent, TEvent>(Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull
            where TEvent : notnull
        {
            return new JsonTransformation(FromDbDataReader(transform), FromDbDataReaderAsync(transform));
        }

        public static Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>>
            FromDbDataReaderAsync<TOldEvent, TEvent>(Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull where TEvent : notnull
        {
            return async (serializer, dbDataReader, index, ct) =>
                transform(await serializer.FromJsonAsync<TOldEvent>(dbDataReader, index, ct)
                    .ConfigureAwait(false));
        }

        public static Func<ISerializer, DbDataReader, int, object> FromDbDataReader<TOldEvent, TEvent>(
            Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull where TEvent : notnull
        {
            return (serializer, dbDataReader, index) => transform(serializer.FromJson<TOldEvent>(dbDataReader, index));
        }
    }
}
