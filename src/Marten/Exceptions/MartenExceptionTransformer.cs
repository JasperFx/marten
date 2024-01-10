using System;
using JasperFx.Core.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Exceptions;
#nullable enable

internal static class MartenExceptionTransformer
{
    static MartenExceptionTransformer()
    {
        Transforms.AddTransform<EventStreamUnexpectedMaxEventIdExceptionTransform>();
        Transforms.AddTransform<MartenCommandNotSupportedExceptionTransform>();
        Transforms.AddTransform<UtcDateTimeUsageExceptionTransform>();
        Transforms.AddTransform<DateTimeUsageExceptionTransform>();
        Transforms.AddTransform<InvalidConnectionStreamExceptionTransform>();

        Transforms.IfExceptionIs<PostgresException>()
            .If(e => e.SqlState == PostgresErrorCodes.SerializationFailure)
            .ThenTransformTo(e => throw new ConcurrentUpdateException(e));

        Transforms.IfExceptionIs<NpgsqlException>()
            .TransformTo(e =>
            {
                var command = e.ReadNpgsqlCommand();
                return new MartenCommandException(command, e);
            });
    }

    public static ExceptionTransforms Transforms { get; } = new();

    internal static NpgsqlCommand? ReadNpgsqlCommand(this Exception ex)
    {
        return ex.Data.Contains(nameof(NpgsqlCommand))
            ? ex.Data[nameof(NpgsqlCommand)] as NpgsqlCommand
            : null;
    }

    internal static void WrapAndThrow(NpgsqlCommand? command, Exception exception)
    {
        if (command != null)
        {
            exception.Data[nameof(NpgsqlCommand)] = command;
        }

        Transforms.TransformAndThrow(exception);
    }

    internal static void WrapAndThrow(Exception exception)
    {
        Transforms.TransformAndThrow(exception);
    }

    public static void WrapAndThrow(NpgsqlBatch batch, Exception exception)
    {
        if (batch != null)
        {
            exception.Data[nameof(NpgsqlBatch)] = batch;
        }

        Transforms.TransformAndThrow(exception);
    }
}
