#nullable enable
using System;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;

namespace Marten.Services.Json.Transformations.SystemTextJson;

public static class JsonTransformations
{
    /// <summary>
    ///     Wrapper for System.Text.Json raw JSON payload upcast function registration
    /// </summary>
    /// <param name="transform">JSON payload transformation</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    /// <returns><see cref="Marten.Services.Json.Transformations.JsonTransformation" /> with upcasting definition</returns>
    public static JsonTransformation Upcast<TEvent>(Func<JsonDocument, TEvent> transform)
        where TEvent : notnull
    {
        return new JsonTransformation(FromDbDataReader(transform));
    }

    /// <summary>
    ///     Wrapper for System.Text.Json async only raw JSON payload upcast function registration
    /// </summary>
    /// <param name="transform">JSON payload transformation</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    /// <returns><see cref="Marten.Services.Json.Transformations.JsonTransformation" /> with upcasting definition</returns>
    /// <exception cref="MartenException">when upcaster is called in sync API</exception>
    public static JsonTransformation AsyncOnlyUpcast<TEvent>(
        Func<JsonDocument, CancellationToken, Task<TEvent>> transform)
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

    internal static Func<ISerializer, DbDataReader, int, object> FromDbDataReader<TEvent>(
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

    internal static Func<ISerializer, DbDataReader, int, CancellationToken, Task<TEvent>>
        FromDbDataReaderAsync<TEvent>(
            Func<JsonDocument, CancellationToken, Task<TEvent>> transform
        ) where TEvent : notnull
    {
        return (serializer, dbDataReader, index, ct) =>
        {
            if (serializer is not SystemTextJsonSerializer systemTextJsonSerializer)
            {
                throw new MartenException(
                    $"Cannot use SystemTextJson upcaster with serializer of type {serializer.GetType().FullName}");
            }

            return transform(systemTextJsonSerializer.JsonDocumentFromJson(dbDataReader, index), ct);
        };
    }
}
