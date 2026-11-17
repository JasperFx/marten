using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core.Exceptions;
using Marten.Services;
using Npgsql;

namespace Marten.Exceptions;

public class MissingGinExtensionException: MartenException
{
    public MissingGinExtensionException(Exception? innerException) : base("Unable to create GIN/GIST index. See https://stackoverflow.com/questions/32138996/why-error-occurred-while-creating-gin-index for a possible remedy", innerException)
    {
    }
}

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

        Transforms.IfExceptionIs<PostgresException>()
            .If(e => e.ErrorCode == 42704)
            .ThenTransformTo(e => throw new MissingGinExtensionException(e));

        Transforms.IfExceptionIs<NpgsqlException>()
            .TransformTo(e =>
            {
                var command = e.ReadNpgsqlCommand();
                return new MartenCommandException(command, e);
            });
    }

    public static ExceptionTransforms Transforms { get; } = new();

    /// <summary>
    ///     #5682: the order every failure is translated in. The failing operations' own transforms first (they
    ///     know the most), then the application's from <see cref="AdvancedOptions.ExceptionTransforms" />, then
    ///     Marten's built-ins, whose last entry turns any <see cref="NpgsqlException" /> into a
    ///     <see cref="MartenCommandException" /> and so would swallow anything the application wanted to see.
    /// </summary>
    internal static IEnumerable<IExceptionTransform> InOrder(ExceptionTransforms? custom,
        IEnumerable<IExceptionTransform>? operations = null)
    {
        IEnumerable<IExceptionTransform> ordered = operations ?? [];
        if (custom != null)
        {
            ordered = ordered.Concat(custom);
        }

        return ordered.Concat(Transforms);
    }

    internal static void WrapAndThrow(ExceptionTransforms? custom, NpgsqlCommand? command, Exception exception)
    {
        if (command != null)
        {
            exception.Data[nameof(NpgsqlCommand)] = command;
        }

        InOrder(custom).TransformAndThrow(exception);
    }

    internal static void WrapAndThrow(ExceptionTransforms? custom, NpgsqlBatch? batch, Exception exception)
    {
        if (batch != null)
        {
            exception.Data[nameof(NpgsqlBatch)] = batch;
        }

        InOrder(custom).TransformAndThrow(exception);
    }

    internal static void WrapAndThrow(ExceptionTransforms? custom, Exception exception)
        => InOrder(custom).TransformAndThrow(exception);

    internal static NpgsqlCommand? ReadNpgsqlCommand(this Exception ex)
    {
        return ex.Data.Contains(nameof(NpgsqlCommand))
            ? ex.Data[nameof(NpgsqlCommand)] as NpgsqlCommand
            : null;
    }

    /// <summary>
    ///     The batch recorded by <see cref="WrapAndThrow(NpgsqlBatch, Exception)" /> or
    ///     <see cref="RecordNpgsqlBatch" />. On the batched write path there is no single
    ///     NpgsqlCommand to report, so this is how <see cref="MartenCommandException" /> gets
    ///     at the SQL.
    /// </summary>
    internal static NpgsqlBatch? ReadNpgsqlBatch(this Exception ex)
    {
        return ex.Data.Contains(nameof(NpgsqlBatch))
            ? ex.Data[nameof(NpgsqlBatch)] as NpgsqlBatch
            : null;
    }

    /// <summary>
    ///     Record the batch an exception came out of so the SQL survives all the way to the
    ///     MartenCommandException message. Call this anywhere a batch is executed without going
    ///     through <see cref="WrapAndThrow(NpgsqlBatch, Exception)" />. The first writer wins, so
    ///     an inner frame that knows the exact failing page is not overwritten by an outer one.
    /// </summary>
    internal static void RecordNpgsqlBatch(this Exception ex, NpgsqlBatch? batch)
    {
        if (batch != null && !ex.Data.Contains(nameof(NpgsqlBatch)))
        {
            ex.Data[nameof(NpgsqlBatch)] = batch;
        }
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
