using System;
using System.Text.Json;
using Marten.Exceptions;

namespace Marten.Services.Json.SystemTextJson
{
    public static class SystemTextJsonEventTransformations
    {
        public static void Upcast(this EventJsonTransformation jsonTransformation, Func<JsonDocument, TEvent> transform)
            where TEvent : notnull
        {
            jsonTransformation.Upcast(
                (serializer, stream) =>
                {
                    if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                    {
                        throw new MartenException(
                            $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                    }

                    return transform(systemTextJsonSerializer.JsonDocumentFromJson(stream));
                },
                (serializer, dbDataReader, index) =>
                {
                    if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                    {
                        throw new MartenException(
                            $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                    }

                    return transform(
                        systemTextJsonSerializer.JsonDocumentFromJson(dbDataReader, index));
                }
            );
        }

        public static void Upcast<TOldEvent, TEvent>(this EventJsonTransformation jsonTransformation, Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull
            where TEvent : notnull
        {
            jsonTransformation.Upcast(
                (serializer, stream) =>
                {
                    if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                    {
                        throw new MartenException(
                            $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                    }

                    return transform(systemTextJsonSerializer.FromJson<TOldEvent>(stream));
                },
                (serializer, dbDataReader, index) =>
                {
                    if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                    {
                        throw new MartenException(
                            $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                    }

                    return transform(systemTextJsonSerializer.FromJson<TOldEvent>(dbDataReader, index));
                }
            );
        }
    }
}
