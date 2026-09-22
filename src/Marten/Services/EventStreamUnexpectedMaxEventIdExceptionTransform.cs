using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Core.Exceptions;
using JasperFx.Events;
using Marten.Events;
using Npgsql;

namespace Marten.Services;

/// <summary>
///     The store-global fallback for a lost optimistic-concurrency race on the rich append path.
/// </summary>
/// <remarks>
///     #5473: this used to be the ONLY translation, and it is the weaker of the two — it sees the
///     <see cref="PostgresException" /> and nothing else, so it can only recover the stream id and
///     version by regex over <c>Detail</c>, which Npgsql redacts unless the connection string
///     carries <c>Include Error Detail=true</c>. <c>PostgresEventStoreDialect.MapAppendEventException</c>
///     now transforms the same violation per-operation, with the <c>StreamAction</c> in hand, so
///     this runs only for a violation that reaches the chain without an operation.
/// </remarks>
internal class EventStreamUnexpectedMaxEventIdExceptionTransform: IExceptionTransform
{
    public bool TryTransform(Exception original, [NotNullWhen(true)] out Exception? transformed)
    {
        if (original is not PostgresException postgresException ||
            !EventVersionConstraint.IsVersionCollision(postgresException))
        {
            transformed = null;
            return false;
        }

        // Unchanged from before #5473: a usable Detail still yields the id and the versions, and
        // `expected` is still derived as actual - 1, including the (id: null, -1, -1) shape when the
        // detail is present but does not match. Only the no-usable-detail branch below changed.
        if (EventVersionConstraint.HasUsableDetail(postgresException))
        {
            var (id, actual) = EventVersionConstraint.ReadDetail(postgresException);
            transformed = new EventStreamUnexpectedMaxEventIdException(id, null, actual - 1, actual);
            return true;
        }

        transformed = new EventStreamUnexpectedMaxEventIdException(BuildDetaillessMessage(postgresException));
        return true;
    }

    /// <summary>
    ///     #5473. Without a usable <c>Detail</c> this transform knows nothing but the constraint that
    ///     fired, and passing <see cref="PostgresException.MessageText" /> straight through handed the
    ///     user Postgres's own sentence — <c>duplicate key value violates unique constraint
    ///     "pk_mt_events_stream_and_version"</c> — which names neither the stream nor the versions and
    ///     does not read as a concurrency failure at all.
    /// </summary>
    internal static string BuildDetaillessMessage(PostgresException postgresException)
    {
        // Reached only when Detail is empty or is exactly Npgsql's redaction sentinel, so a
        // non-empty Detail here means redaction and nothing else.
        var redacted = !string.IsNullOrEmpty(postgresException.Detail);

        return
            "Optimistic concurrency failure appending to an event stream: the expected version did not match the stream's current version. " +
            (redacted
                ? "The stream id and version were redacted by Npgsql; add 'Include Error Detail=true' to the connection string (development and test only) to include them. "
                : "PostgreSQL reported no detail for the violation, so the stream id and version are not available here. ") +
            $"The underlying violation was on '{postgresException.ConstraintName}'.";
    }
}
