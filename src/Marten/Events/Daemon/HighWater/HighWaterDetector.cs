using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;

namespace Marten.Events.Daemon.HighWater
{
    internal class HighWaterDetector: IHighWaterDetector
    {
        private readonly ITenant _tenant;
        private readonly NpgsqlCommand _gapDetection;
        private readonly NpgsqlCommand _stateDetection;
        private readonly NpgsqlParameter _start;
        private readonly NpgsqlCommand _updateStatus;
        private readonly NpgsqlParameter _newSeq;
        private readonly NpgsqlCommand _findSafeSequence;
        private readonly NpgsqlParameter _safeTimestamp;

        public HighWaterDetector(ITenant tenant, EventGraph graph)
        {
            _tenant = tenant;

            _findSafeSequence = new NpgsqlCommand($@"select min(seq_id) from {graph.DatabaseSchemaName}.mt_events where mt_events.timestamp >= :timestamp");
            _safeTimestamp = _findSafeSequence.AddNamedParameter("timestamp", DateTimeOffset.MinValue);

            _gapDetection = new NpgsqlCommand($@"
select seq_id
from   (select
               seq_id,
               lead(seq_id)
               over (order by seq_id) as no
        from
               {graph.DatabaseSchemaName}.mt_events where seq_id > :start) ct
where  no is not null
  and    no - seq_id > 1
LIMIT 1;
select max(seq_id) from {graph.DatabaseSchemaName}.mt_events where seq_id > :start;
".Trim());

            _start = _gapDetection.AddNamedParameter("start", 0L);

            _stateDetection = new NpgsqlCommand($@"
select last_value from {graph.DatabaseSchemaName}.mt_events_sequence;
select last_seq_id, last_updated, transaction_timestamp() as timestamp from {graph.DatabaseSchemaName}.mt_event_progression where name = '{ShardState.HighWaterMark}';
".Trim());

            _updateStatus =
                new NpgsqlCommand($"select {graph.DatabaseSchemaName}.mt_mark_event_progression('{ShardState.HighWaterMark}', :seq);");
            _newSeq = _updateStatus.AddNamedParameter("seq", 0L);

        }

        public async Task<HighWaterStatistics> DetectInSafeZone(DateTimeOffset safeTimestamp, CancellationToken token)
        {
            await using var conn = _tenant.OpenConnection();

            var statistics = await loadCurrentStatistics(conn, token);

            _safeTimestamp.Value = safeTimestamp;
            using (var reader = await conn.ExecuteReaderAsync(_findSafeSequence, token))
            {
                if (await reader.ReadAsync(token))
                {
                    statistics.SafeStartMark = await reader.GetFieldValueAsync<long>(0, token);
                }
            }

            await calculateHighWaterMark(token, statistics, conn);

            return statistics;
        }


        public async Task<HighWaterStatistics> Detect(CancellationToken token)
        {
            await using var conn = _tenant.OpenConnection();

            var statistics = await loadCurrentStatistics(conn, token);


            await calculateHighWaterMark(token, statistics, conn);

            return statistics;
        }

        private async Task calculateHighWaterMark(CancellationToken token, HighWaterStatistics statistics,
            IManagedConnection conn)
        {
            // If the last high water mark is the same as the highest number
            // assigned from the sequence, then the high water mark cannot
            // have changed
            if (statistics.LastMark == statistics.HighestSequence)
            {
                statistics.CurrentMark = statistics.LastMark;
            }
            else if (statistics.HighestSequence == 0)
            {
                statistics.CurrentMark = statistics.LastMark = 0;
            }
            else
            {
                statistics.CurrentMark = await findCurrentMark(statistics, conn, token);
            }

            if (statistics.HasChanged)
            {
                await conn.BeginTransactionAsync(token);
                _newSeq.Value = statistics.CurrentMark;
                await conn.ExecuteAsync(_updateStatus, token);
                await conn.CommitAsync(token);

                if (!statistics.LastUpdated.HasValue)
                {
                    var current = await loadCurrentStatistics(conn, token);
                    statistics.LastUpdated = current.LastUpdated;
                }
            }
        }

        private async Task<HighWaterStatistics> loadCurrentStatistics(IManagedConnection conn, CancellationToken token)
        {
            var statistics = new HighWaterStatistics();

            using var reader = await conn.ExecuteReaderAsync(_stateDetection, token);
            if (await reader.ReadAsync(token))
            {
                statistics.HighestSequence = await reader.GetFieldValueAsync<long>(0, token);
            }

            await reader.NextResultAsync(token);

            if (!await reader.ReadAsync(token)) return statistics;

            statistics.LastMark = statistics.SafeStartMark = await reader.GetFieldValueAsync<long>(0, token);
            statistics.LastUpdated = await reader.GetFieldValueAsync<DateTimeOffset>(1, token);
            statistics.Timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(2, token);

            return statistics;
        }

        private async Task<long> findCurrentMark(HighWaterStatistics statistics, IManagedConnection conn, CancellationToken token)
        {
            // look for the current mark
            _start.Value = statistics.SafeStartMark;
            using var reader = await conn.ExecuteReaderAsync(_gapDetection, token);

            // If there is a row, this tells us the first sequence gap
            if (await reader.ReadAsync(token))
            {
                return await reader.GetFieldValueAsync<long>(0, token);
            }

            // use the latest sequence in the event table
            await reader.NextResultAsync(token);
            if (!await reader.ReadAsync(token)) return statistics.CurrentMark;

            if (!(await reader.IsDBNullAsync(0, token)))
            {
                return await reader.GetFieldValueAsync<long>(0, token);
            }

            return statistics.CurrentMark;
        }

    }
}
