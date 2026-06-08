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
        // #4681: the literal 'HighWaterMark' name is produced by HighWaterShardIdentity so
        // any future change to the grammar (e.g. an alternate store-global name) lands in
        // one place rather than scattered SQL string literals.
        _commandText = $@"
select last_value from {graph.DatabaseSchemaName}.mt_events_sequence;
select last_seq_id, last_updated, transaction_timestamp() as timestamp from {graph.DatabaseSchemaName}.mt_event_progression where name = '{HighWaterShardIdentity.StoreGlobal}';
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
        }

        await reader.NextResultAsync(token).ConfigureAwait(false);

        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return statistics;
        }

        statistics.LastMark = statistics.SafeStartMark =
            await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        statistics.LastUpdated = await reader.GetFieldValueAsync<DateTimeOffset>(1, token).ConfigureAwait(false);
        statistics.Timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(2, token).ConfigureAwait(false);

        return statistics;
    }
}
