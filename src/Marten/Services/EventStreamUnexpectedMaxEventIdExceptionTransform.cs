using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using JasperFx.Core;
using JasperFx.Core.Exceptions;
using JasperFx.Events;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Services;

internal class EventStreamUnexpectedMaxEventIdExceptionTransform: IExceptionTransform
{
    private const string DetailsRedactedMessage = "Detail redacted as it may contain sensitive data. " +
        "Specify 'Include Error Detail' in the connection string to include this information.";

    private const string StreamId = "streamid";
    private const string Version = "version";

    public bool TryTransform(Exception original, [NotNullWhen(true)] out Exception? transformed)
    {
        if (!Matches(original))
        {
            transformed = null;
            return false;
        }

        var postgresException = original as PostgresException;

        object? id = null;
        Type? aggregateType = null;
        var expected = -1;
        var actual = -1;

        if (!string.IsNullOrEmpty(postgresException.Detail) && !postgresException.Detail.EqualsIgnoreCase(DetailsRedactedMessage))
        {
            var details = EventStreamUnexpectedMaxEventIdExceptionTransformRegexExpressions.EventStreamUniqueExceptionDetailsRegex().Match(postgresException.Detail);

            if (details.Groups[StreamId].Success)
            {
                var streamId = details.Groups[StreamId].Value;

                id = Guid.TryParse(streamId, out var guidStreamId) ? guidStreamId : streamId;
            }

            if (details.Groups[Version].Success)
            {
                var actualVersion = details.Groups[Version].Value;

                if (int.TryParse(actualVersion, out var actualIntVersion))
                {
                    actual = actualIntVersion;
                    expected = actual - 1;
                }
            }

            transformed = new EventStreamUnexpectedMaxEventIdException(id, aggregateType, expected, actual);
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
    ///     does not read as a concurrency failure at all. Npgsql redacts <c>Detail</c> unless the
    ///     connection string carries <c>Include Error Detail=true</c>, which a production connection
    ///     string generally should not, so this is the message most deployments actually see.
    /// </summary>
    internal static string BuildDetaillessMessage(PostgresException postgresException)
    {
        // This is only reached when Detail is empty or is exactly Npgsql's redaction sentinel, so a
        // non-empty Detail here means redaction and nothing else.
        var redacted = !string.IsNullOrEmpty(postgresException.Detail);

        return
            "Optimistic concurrency failure appending to an event stream: the expected version did not match the stream's current version. " +
            (redacted
                ? "The stream id and version were redacted by Npgsql; add 'Include Error Detail=true' to the connection string (development and test only) to include them. "
                : "PostgreSQL reported no detail for the violation, so the stream id and version are not available here. ") +
            $"The underlying violation was on '{postgresException.ConstraintName}'.";
    }

    /// <summary>
    ///     The unique index that guards one version per stream. Marten always names it
    ///     <c>pk_mt_events_stream_and_version</c>, but under
    ///     <see cref="Events.EventGraph.UseArchivedStreamPartitioning" /> the index is partitioned and
    ///     PostgreSQL reports the CHILD index, whose name it generates from the partition and the
    ///     indexed columns. Those columns differ by tenancy style, so there is a name per configuration:
    ///     <list type="bullet">
    ///         <item><description>no partitioning — <c>pk_mt_events_stream_and_version</c></description></item>
    ///         <item><description>partitioned — <c>mt_events_default_stream_id_version_is_archived_idx</c></description></item>
    ///         <item><description>partitioned + conjoined — <c>mt_events_default_tenant_id_stream_id_version_is_archived_idx</c></description></item>
    ///     </list>
    /// </summary>
    /// <remarks>
    ///     #5270. Matched by shape rather than by an enumerated list, because enumerating is what let this
    ///     through twice: #3520 added the second name and the third was still missing, and each partition
    ///     other than <c>_default</c> produces another one again. The <c>stream_id_version</c> requirement
    ///     is what keeps the OTHER unique index on this table — the one over <c>id</c>, whose child is
    ///     <c>mt_events_default_id_idx</c> — from being transformed: a duplicate event id is not an
    ///     optimistic-concurrency conflict and must keep surfacing as itself.
    /// </remarks>
    private static bool Matches(Exception e)
    {
        if (e is not PostgresException pe || pe.SqlState != PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }

        if (pe.ConstraintName is not { } name)
        {
            return false;
        }

        return name == "pk_mt_events_stream_and_version"
               || (name.StartsWith("mt_events", StringComparison.Ordinal)
                   && name.EndsWith("_idx", StringComparison.Ordinal)
                   && name.Contains("stream_id_version", StringComparison.Ordinal));
    }
}

internal static partial class EventStreamUnexpectedMaxEventIdExceptionTransformRegexExpressions
{
    [GeneratedRegex(@"\(stream_id, version\)=\((?<streamid>.*?), (?<version>\w+)\)")]
    internal static partial Regex EventStreamUniqueExceptionDetailsRegex();
}
