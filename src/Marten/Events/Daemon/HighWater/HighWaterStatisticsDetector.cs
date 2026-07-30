using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon.HighWater;
using Marten.Services;
using Npgsql;

namespace Marten.Events.Daemon.HighWater;

/// <summary>
/// #4953: Marten-side extension of the JasperFx statistics reading that also carries the xmax of the
/// database snapshot the statistics were read under. The high-water detector records this when it
/// first observes a stuck sequence gap: any in-progress write transaction with an xid below this
/// value existed when the gap was observed and is therefore a candidate reserver of the gap — the
/// gap cannot be treated as permanently dead while any such transaction is still running.
/// </summary>
internal class MartenHighWaterStatistics: HighWaterStatistics
{
    public long CurrentXmax { get; set; }

    /// <summary>
    /// #5091: the highest sequence number the event sequence has actually HANDED OUT, as opposed to
    /// <see cref="HighWaterStatistics.HighestSequence" />, which is the reserved ceiling. Postgres
    /// reports <c>last_value = 1, is_called = false</c> for a sequence nothing has drawn from yet, so
    /// the reserved ceiling never reads below 1 and a gap stuck at mark 0 could never be fenced. Null
    /// when no sound reading exists — under per-tenant event partitioning the store-global sequence is
    /// not the one being drawn from, so nothing can be concluded about allocation from it.
    /// </summary>
    public long? AllocatedSequenceHigh { get; set; }
}

internal class HighWaterStatisticsDetector: ISingleQueryHandler<HighWaterStatistics>
{
    private readonly string _commandText;

    public HighWaterStatisticsDetector(EventGraph graph)
    {
        // #4712 (same bug class as #4705): under per-tenant event partitioning the store-global
        // mt_events_sequence is never advanced — each tenant draws seq_ids from its own
        // mt_events_sequence_{suffix} — so `last_value` reports a stale 1 while the true height is
        // far higher. That made the store-global high-water agent treat the store as perpetually
        // Stale. Read the real height from mt_events instead (mirrors FetchHighestEventSequenceNumber).
        var highestSequenceSql = graph.UseTenantPartitionedEvents
            ? $"(select coalesce(max(seq_id), 0) from {graph.DatabaseSchemaName}.mt_events)"
            : $"(select last_value from {graph.DatabaseSchemaName}.mt_events_sequence)";

        // #5091: the highest ALLOCATED sequence number, for the #4953 allocation fence. `last_value`
        // is the reserved ceiling and reads 1 on a sequence nothing has drawn from yet (is_called =
        // false), so a fence built from it could never be established for a gap stuck at mark 0 —
        // exactly the shape in #5090. `is_called` distinguishes "1 has been handed out" from "1 is
        // the next value to hand out", which makes a pristine sequence report an allocated high of 0.
        // Under per-tenant partitioning the store-global sequence is not the one tenants draw from,
        // so there is no sound allocation reading at all — NULL, and no fence (same posture the
        // detector already takes for that case).
        var allocatedHighSql = graph.UseTenantPartitionedEvents
            ? "null::bigint"
            : $"(select case when is_called then last_value else last_value - 1 end from {graph.DatabaseSchemaName}.mt_events_sequence)";

        // #4953: a single statement so every reading comes from ONE snapshot (see GapDetector for the
        // multi-statement snapshot-skew hazard this rules out). The LEFT JOIN from a one-row VALUES
        // clause preserves the #4712 guarantee that exactly one row always comes back with a real
        // Timestamp even when the store-global progression row does not exist yet — the missing-row
        // default(DateTimeOffset) Timestamp was what produced the bogus 0001-01-01 SafeHarborTime.
        // pg_snapshot_xmax feeds the #4953 outstanding-transaction fencing (see MartenHighWaterStatistics).
        // #4681: the literal 'HighWaterMark' name is produced by HighWaterShardIdentity so any future
        // change to the grammar lands in one place rather than scattered SQL string literals.
        _commandText = $@"
select
  {highestSequenceSql} as highest_sequence,
  transaction_timestamp() as ""timestamp"",
  pg_snapshot_xmax(pg_current_snapshot())::text::bigint as current_xmax,
  p.last_seq_id,
  p.last_updated,
  {allocatedHighSql} as allocated_high
from (values (1)) as one(x)
left join {graph.DatabaseSchemaName}.mt_event_progression p
  on p.name = '{HighWaterShardIdentity.StoreGlobal}'
".Trim();
    }

    public NpgsqlCommand BuildCommand()
    {
        return new NpgsqlCommand(_commandText);
    }

    public async Task<HighWaterStatistics> HandleAsync(DbDataReader reader, CancellationToken token)
    {
        var statistics = new MartenHighWaterStatistics();

        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return statistics;
        }

        statistics.HighestSequence = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        statistics.Timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(1, token).ConfigureAwait(false);
        statistics.CurrentXmax = await reader.GetFieldValueAsync<long>(2, token).ConfigureAwait(false);

        if (!await reader.IsDBNullAsync(3, token).ConfigureAwait(false))
        {
            statistics.LastMark = statistics.SafeStartMark =
                await reader.GetFieldValueAsync<long>(3, token).ConfigureAwait(false);
            statistics.LastUpdated = await reader.GetFieldValueAsync<DateTimeOffset>(4, token).ConfigureAwait(false);
        }

        if (!await reader.IsDBNullAsync(5, token).ConfigureAwait(false))
        {
            statistics.AllocatedSequenceHigh = await reader.GetFieldValueAsync<long>(5, token).ConfigureAwait(false);
        }

        return statistics;
    }
}
