#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Newtonsoft.Json.Linq;

namespace Marten.Services.Json.Transformations.JsonNet
{
    public static class JsonTransformations
    {
        public static JsonTransformation Upcast<TEvent>(Func<JObject, TEvent> transform)
            where TEvent : notnull
        {
            return new JsonTransformation(FromDbDataReader(transform));
        }

        public static JsonTransformation Upcast<TEvent>(Func<JObject, CancellationToken, Task<TEvent>> transform)
            where TEvent : notnull
        {
            return new JsonTransformation(
                (_, _, _) =>
                    throw new MartenException(
                        $"Cannot use JSON transformation to event '{typeof(TEvent)}' in the synchronous API" +
                        "It was defined as async only"
                    ),
                async (serializer, reader, index, ct) =>
                    await FromDbDataReaderAsync(transform)(serializer, reader, index, ct).ConfigureAwait(false)
            );
        }

        public static Func<ISerializer, DbDataReader, int, object> FromDbDataReader<TEvent>(
            Func<JObject, TEvent> transform
        ) where TEvent : notnull
        {
            return (serializer, dbDataReader, index) =>
            {
                if (serializer is not JsonNetSerializer jsonNetSerializer)
                {
                    throw new MartenException(
                        $"Cannot use JsonNet upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(
                    jsonNetSerializer.JObjectFromJson(dbDataReader, index));
            };
        }

        public static Func<ISerializer, DbDataReader, int, CancellationToken, Task<TEvent>>
            FromDbDataReaderAsync<TEvent>(
                Func<JObject, CancellationToken, Task<TEvent>> transform
            ) where TEvent : notnull
        {
            return (serializer, dbDataReader, index, ct) =>
            {
                if (serializer is not JsonNetSerializer jsonNetSerializer)
                {
                    throw new MartenException(
                        $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(jsonNetSerializer.JObjectFromJson(dbDataReader, index), ct);
            };
        }
    }
}
