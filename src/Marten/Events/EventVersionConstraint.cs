#nullable enable
using System;
using System.Text.RegularExpressions;
using Npgsql;

namespace Marten.Events;

/// <summary>
///     The unique index that guards one version per stream, and how to read a lost race off the
///     <see cref="PostgresException" /> it raises.
/// </summary>
/// <remarks>
///     <para>
///         #5473 moved this out of <c>EventStreamUnexpectedMaxEventIdExceptionTransform</c> so the
///         per-operation transform on the rich append path (<c>PostgresEventStoreDialect</c>) and
///         the global fallback chain recognise the SAME violation. Two copies of this predicate is
///         precisely the failure mode #5270 documents: the shape test replaced an enumerated list of
///         index names because enumerating let the bug through twice.
///     </para>
/// </remarks>
internal static class EventVersionConstraint
{
    /// <summary>
    ///     Npgsql's stand-in when <c>Include Error Detail</c> is off, which a production connection
    ///     string generally leaves off. Matched so a redacted detail is not mistaken for a real one.
    /// </summary>
    internal const string DetailsRedactedMessage = "Detail redacted as it may contain sensitive data. " +
        "Specify 'Include Error Detail' in the connection string to include this information.";

    private const string StreamIdGroup = "streamid";
    private const string VersionGroup = "version";

    /// <summary>
    ///     Marten always names the index <c>pk_mt_events_stream_and_version</c>, but under
    ///     <see cref="EventGraph.UseArchivedStreamPartitioning" /> the index is partitioned and
    ///     PostgreSQL reports the CHILD index, whose name it generates from the partition and the
    ///     indexed columns. Those columns differ by tenancy style, so there is a name per
    ///     configuration:
    ///     <list type="bullet">
    ///         <item><description>no partitioning — <c>pk_mt_events_stream_and_version</c></description></item>
    ///         <item><description>partitioned — <c>mt_events_default_stream_id_version_is_archived_idx</c></description></item>
    ///         <item><description>partitioned + conjoined — <c>mt_events_default_tenant_id_stream_id_version_is_archived_idx</c></description></item>
    ///     </list>
    /// </summary>
    /// <remarks>
    ///     #5270. Matched by shape rather than by an enumerated list, because enumerating is what let
    ///     this through twice: #3520 added the second name and the third was still missing, and each
    ///     partition other than <c>_default</c> produces another one again. The
    ///     <c>stream_id_version</c> requirement is what keeps the OTHER unique index on this table —
    ///     the one over <c>id</c>, whose child is <c>mt_events_default_id_idx</c> — from being
    ///     transformed: a duplicate event id is not an optimistic-concurrency conflict and must keep
    ///     surfacing as itself.
    /// </remarks>
    internal static bool IsVersionCollision(Exception e)
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

    /// <summary>
    ///     Whether <c>Detail</c> is worth parsing at all: present, and not Npgsql's redaction
    ///     sentinel. False is the normal production case, and the reason the per-operation transform
    ///     on the rich append path exists.
    /// </summary>
    internal static bool HasUsableDetail(PostgresException pe)
        => !string.IsNullOrEmpty(pe.Detail)
           && !string.Equals(pe.Detail, DetailsRedactedMessage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Reads the stream id and the version that was actually taken out of the exception's
    ///     <c>Detail</c>. Returns <c>(null, -1)</c> when the detail is unusable or does not match.
    /// </summary>
    internal static (object? Id, int Actual) ReadDetail(PostgresException pe)
    {
        if (!HasUsableDetail(pe))
        {
            return (null, -1);
        }

        var match = EventVersionConstraintRegex.DetailRegex().Match(pe.Detail!);

        object? id = null;
        if (match.Groups[StreamIdGroup].Success)
        {
            var streamId = match.Groups[StreamIdGroup].Value;
            id = Guid.TryParse(streamId, out var guidStreamId) ? guidStreamId : streamId;
        }

        var actual = -1;
        if (match.Groups[VersionGroup].Success && int.TryParse(match.Groups[VersionGroup].Value, out var parsed))
        {
            actual = parsed;
        }

        return (id, actual);
    }
}

internal static partial class EventVersionConstraintRegex
{
    [GeneratedRegex(@"\(stream_id, version\)=\((?<streamid>.*?), (?<version>\w+)\)")]
    internal static partial Regex DetailRegex();
}
