#nullable enable
using System;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;

namespace Marten.Services.Json.Transformations.SystemTextJson
{
    public static class Transformations
    {
        public static JsonTransformation Upcast<TEvent>(Func<JsonDocument, TEvent> transform)
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
            Func<JsonDocument, TEvent> transform) where TEvent : notnull
        {
            return (serializer, dbDataReader, index) =>
            {
                if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                {
                    throw new MartenException(
                        $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(
                    systemTextJsonSerializer.JsonDocumentFromJson(dbDataReader, index));
            };
        }

        public static Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>>
            FromDbDataReaderAsync<TOldEvent, TEvent>(Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull where TEvent : notnull
        {
            return async (serializer, dbDataReader, index, ct) =>
            {
                if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                {
                    throw new MartenException(
                        $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(await systemTextJsonSerializer.FromJsonAsync<TOldEvent>(dbDataReader, index, ct)
                    .ConfigureAwait(false));
            };
        }

        public static Func<ISerializer, DbDataReader, int, object> FromDbDataReader<TOldEvent, TEvent>(
            Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull where TEvent : notnull
        {
            return (serializer, dbDataReader, index) =>
            {
                if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                {
                    throw new MartenException(
                        $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                }

                return transform(systemTextJsonSerializer.FromJson<TOldEvent>(dbDataReader, index));
            };
        }
    }
}
