#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Newtonsoft.Json.Linq;

namespace Marten.Services.Json.Transformations.JsonNet
{
    public static class Transformations
    {
        public static JsonTransformation Upcast<TEvent>(Func<JObject, TEvent> transform)
            where TEvent : notnull
        {
            return new JsonTransformation(FromDbDataReader(transform));
        }

        public static JsonTransformation Upcast<TOldEvent, TEvent>(Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull
            where TEvent : notnull
        {
            return new JsonTransformation(FromDbDataReader(transform), FromDbDataReaderAsync(transform));
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

        public static Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>>
            FromDbDataReaderAsync<TOldEvent, TEvent>(Func<TOldEvent, TEvent> transform
        ) where TOldEvent : notnull where TEvent : notnull
        {
            return async (serializer, dbDataReader, index, ct) =>
            {
                if (serializer is not JsonNetSerializer jsonNetSerializer)
                {
                    throw new MartenException(
                        $"Cannot use JsonNet upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(await jsonNetSerializer.FromJsonAsync<TOldEvent>(dbDataReader, index, ct)
                    .ConfigureAwait(false));
            };
        }

        public static Func<ISerializer, DbDataReader, int, object> FromDbDataReader<TOldEvent, TEvent>(
            Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull where TEvent : notnull
        {
            return (serializer, dbDataReader, index) =>
            {
                if (serializer is not JsonNetSerializer jsonNetSerializer)
                {
                    throw new MartenException(
                        $"Cannot use JsonNet upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(jsonNetSerializer.FromJson<TOldEvent>(dbDataReader, index));
            };
        }
    }
}
