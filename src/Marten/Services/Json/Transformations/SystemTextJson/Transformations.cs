#nullable enable
using System;
using System.Data.Common;
using System.Text.Json;
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

        public static Func<ISerializer, DbDataReader, int, object> FromDbDataReader<TEvent>(
            Func<JsonDocument, TEvent> transform
        ) where TEvent : notnull
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
    }
}
