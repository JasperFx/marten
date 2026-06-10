using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon.HighWater;
using Marten.Services;
using Npgsql;

namespace Marten.Events.Daemon.HighWater;

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
            ? $"select coalesce(max(seq_id), 0), transaction_timestamp() from {graph.DatabaseSchemaName}.mt_events"
            : $"select last_value, transaction_timestamp() from {graph.DatabaseSchemaName}.mt_events_sequence";

        // #4712: stamp Timestamp from THIS first result, which always returns exactly one row. Reading
        // the timestamp off the second (mt_event_progression) result left Timestamp at
        // default(DateTimeOffset) = 0001-01-01 whenever the store-global 'HighWaterMark' progression
        // row was absent — and that default is exactly what produced the bogus SafeHarborTime
        // (0001-01-01 + 3s threshold) that turned the gap-skip into a no-op and hung composite rebuilds.
        // #4681: the literal 'HighWaterMark' name is produced by HighWaterShardIdentity so any future
        // change to the grammar lands in one place rather than scattered SQL string literals.
        _commandText = $@"
{highestSequenceSql};
select last_seq_id, last_updated from {graph.DatabaseSchemaName}.mt_event_progression where name = '{HighWaterShardIdentity.StoreGlobal}';
".Trim();
    }

    public NpgsqlCommand BuildCommand()
    {
        return new NpgsqlCommand(_commandText);
    }

    public async Task<HighWaterStatistics> HandleAsync(DbDataReader reader, CancellationToken token)
    {
        var statistics = new HighWaterStatistics();

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            statistics.HighestSequence = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            statistics.Timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(1, token).ConfigureAwait(false);
        }

        await reader.NextResultAsync(token).ConfigureAwait(false);

        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return statistics;
        }

        statistics.LastMark = statistics.SafeStartMark =
            await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        statistics.LastUpdated = await reader.GetFieldValueAsync<DateTimeOffset>(1, token).ConfigureAwait(false);

        return statistics;
    }
}
