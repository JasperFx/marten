using System;
using System.Text.Json;
using Marten.Exceptions;

namespace Marten.Services.Json.SystemTextJson
{
    public static class SystemTextJsonUpcasters
    {
        public static void With<TEvent>(this EventUpcaster upcaster, Func<JsonDocument, TEvent> transform)
            where TEvent : notnull
        {
            upcaster.With(typeof(TEvent),
                (serializer, stream) =>
                {
                    if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                    {
                        throw new MartenException(
                            $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                    }

                    return transform(systemTextJsonSerializer.JsonDocumentFromJson(typeof(TEvent), stream));
                },
                (serializer, dbDataReader, index) =>
                {
                    if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
                    {
                        throw new MartenException(
                            $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
                    }

                    return transform(
                        systemTextJsonSerializer.JsonDocumentFromJson(typeof(TEvent), dbDataReader, index));
                }
            );
        }

        public static void With<TOldEvent, TEvent>(this EventUpcaster upcaster, Func<TOldEvent, TEvent> transform)
            where TOldEvent : notnull
            where TEvent : notnull
        {
            upcaster.With(typeof(TEvent),
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
