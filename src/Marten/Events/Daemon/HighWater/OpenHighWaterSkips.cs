#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Schema;
using Marten.Internal;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Daemon.HighWater;

/// <summary>
///     #6114: one open skip of the high water mark with recorded gaps that hold some of the events a delete is about
///     to remove.
/// </summary>
/// <param name="Starting">The skipped range is (<paramref name="Starting" />, <paramref name="Ending" />].</param>
/// <param name="Ending">The high water mark the skip moved to.</param>
/// <param name="TenantId">The tenant whose own mark skipped, or the empty string for the store-global mark.</param>
/// <param name="Reason">The <see cref="HighWaterSkipReason" />, null for a skip recorded without one.</param>
/// <param name="SkippedAt">When the skip was recorded.</param>
/// <param name="Sequences">The events about to be deleted that sit in the skip's gaps.</param>
internal sealed record OpenHighWaterSkip(long Starting, long Ending, string TenantId, string? Reason,
    DateTimeOffset? SkippedAt, long[] Sequences);

/// <summary>
///     #6114: the open skips of the high water mark that a delete of events must wait for. Shared by stream
///     compaction and the cold move (#5710), so the two guards apply one rule.
/// </summary>
/// <remarks>
///     <para>
///         The lag guards of both deletes take "the shard's <c>last_seq_id</c> is at or past the highest deleted
///         <c>seq_id</c>" as proof that the shard processed the events. After a high water skip (#6007) that is not
///         true: an event that commits late inside a skipped range has a <c>seq_id</c> the shards already moved past.
///         <see cref="LateEventRecovery" /> finds such an event by checking the skip's recorded gaps until it closes
///         the skip, <see cref="Projections.ProjectionOptions.LateEventDetectionWindow" /> after it was recorded. A
///         delete that removes the event first leaves recovery nothing to find.
///     </para>
///     <para>
///         The rule is recovery's own: a skip is open while <c>closed_at</c> is null, and an event is at risk when
///         its <c>seq_id</c> is inside one of the skip's recorded gaps, for the same tenant match recovery uses. An
///         event outside the gaps had already committed when the range was skipped, so the shards read it. Nothing
///         is checked without <see cref="EventGraph.EnableAdvancedAsyncTracking" />, because nothing ever closes a
///         skip then and refusing could not help recovery.
///     </para>
/// </remarks>
internal static class OpenHighWaterSkips
{
    /// <summary>
    ///     The SQL that selects every open skip with a recorded gap holding one of <paramref name="sequencesSql" />,
    ///     one row per skip: <c>starting_sequence, ending_sequence, tenant_id, reason, timestamp, sequences</c>.
    /// </summary>
    /// <param name="sequencesSql">A query or set-returning expression with one bigint column: the events to delete.</param>
    /// <param name="tenantSql">An expression for the tenant id of those events, usually a parameter.</param>
    public static string CoveringSql(EventGraph events, string sequencesSql, string tenantSql)
    {
        // The tenant match is LateEventRecovery's: under per-tenant partitioned events every tenant draws its own
        // sequence, so only that tenant's own skips apply. Otherwise a store-global skip covers every tenant.
        var tenantMatch = events.UseTenantPartitionedEvents
            ? $"s.tenant_id = {tenantSql}"
            : $"(s.tenant_id = '{EventProgressionSkippingTable.GlobalTenant}' or s.tenant_id = {tenantSql})";

        return $@"select s.starting_sequence, s.ending_sequence, s.tenant_id, s.reason, s.timestamp,
       array_agg(distinct d.seq order by d.seq) as sequences
from {events.DatabaseSchemaName}.{EventProgressionSkippingTable.Name} s
cross join lateral generate_series(1, coalesce(cardinality(s.gaps), 0) - 1, 2) as i(n)
join ({sequencesSql}) as d(seq) on d.seq >= s.gaps[i.n] and d.seq <= s.gaps[i.n + 1]
where s.closed_at is null and {tenantMatch}
group by s.starting_sequence, s.ending_sequence, s.tenant_id, s.reason, s.timestamp
order by s.ending_sequence";
    }

    /// <summary>
    ///     The open skips with a recorded gap holding one of <paramref name="sequences" />, read through the session's
    ///     own connection. Empty when advanced async tracking is off.
    /// </summary>
    public static async Task<IReadOnlyList<OpenHighWaterSkip>> FindCoveringAsync(IMartenSession session,
        IReadOnlyCollection<long> sequences, string tenantId, CancellationToken token)
    {
        var events = session.Options.EventGraph;
        if (!events.EnableAdvancedAsyncTracking || sequences.Count == 0) return [];

        var cmd = new NpgsqlCommand(CoveringSql(events, "select unnest(@sequences)", "@tenant"));
        cmd.Parameters.Add(new NpgsqlParameter("sequences", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = sequences.ToArray()
        });
        cmd.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Varchar) { Value = tenantId });

        var skips = new List<OpenHighWaterSkip>();
        await using var reader = await session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            skips.Add(new OpenHighWaterSkip(
                await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false),
                await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false),
                await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false),
                await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<string>(3, token).ConfigureAwait(false),
                await reader.IsDBNullAsync(4, token).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<DateTimeOffset>(4, token).ConfigureAwait(false),
                await reader.GetFieldValueAsync<long[]>(5, token).ConfigureAwait(false)));
        }

        return skips;
    }

    /// <summary>
    ///     Why a delete has to wait for <paramref name="skips" />, in plain words: which events, which skip and its
    ///     range, and when recovery will close it. Both guards put this in their refusal.
    /// </summary>
    public static string Describe(IReadOnlyList<OpenHighWaterSkip> skips, EventGraph events)
    {
        var window = events.Options.Projections.LateEventDetectionWindow;

        var each = skips.Select(skip =>
        {
            var mark = skip.TenantId == EventProgressionSkippingTable.GlobalTenant
                ? "the high water mark"
                : $"tenant '{skip.TenantId}''s high water mark";

            var when = skip.SkippedAt.HasValue ? $" at {skip.SkippedAt.Value.UtcDateTime:u}" : "";
            var reason = skip.Reason == null ? "" : $" ({skip.Reason})";

            var closes = skip.SkippedAt.HasValue
                ? $"Late event recovery in the async daemon closes that skip on its first check after {(skip.SkippedAt.Value + window).UtcDateTime:u} (LateEventDetectionWindow is {window})"
                : "Late event recovery in the async daemon closes that skip on its next check";

            return $"event(s) {string.Join(", ", skip.Sequences)} sit in a gap of the sequence range ({skip.Starting}, {skip.Ending}] " +
                   $"that {mark} skipped{when}{reason}, so they may have committed after the async projections and " +
                   $"subscriptions moved past them, and late event recovery has not finished checking that range. {closes}.";
        });

        return string.Join(" Also, ", each);
    }
}
