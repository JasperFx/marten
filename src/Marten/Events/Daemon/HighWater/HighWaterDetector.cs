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
        private AutoOpenSingleQueryRunner _runner;
        private readonly NpgsqlCommand _updateStatus;
        private readonly NpgsqlParameter _newSeq;
        private GapDetector _gapDetector;
        private SafeSequenceFinder _safeSequenceFinder;
        private HighWaterStatisticsDetector _highWaterStatisticsDetector;

        public HighWaterDetector(ITenant tenant, EventGraph graph)
        {
            // TODO -- this will need to be injected later.
            _runner = new AutoOpenSingleQueryRunner(tenant);
            _gapDetector = new GapDetector(graph);
            _safeSequenceFinder = new SafeSequenceFinder(graph);
            _highWaterStatisticsDetector = new HighWaterStatisticsDetector(graph);

            _tenant = tenant;

            _updateStatus =
                new NpgsqlCommand($"select {graph.DatabaseSchemaName}.mt_mark_event_progression('{ShardState.HighWaterMark}', :seq);");
            _newSeq = _updateStatus.AddNamedParameter("seq", 0L);

        }

        public async Task<HighWaterStatistics> DetectInSafeZone(DateTimeOffset safeTimestamp, CancellationToken token)
        {
            await using var conn = _tenant.OpenConnection();

            var statistics = await loadCurrentStatistics(conn, token);

            _safeSequenceFinder.SafeTimestamp = safeTimestamp;
            var safeSequence = await _runner.Query(_safeSequenceFinder, token);
            if (safeSequence.HasValue)
            {
                statistics.SafeStartMark = safeSequence.Value;
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
            return await _runner.Query(_highWaterStatisticsDetector, token);
        }

        private async Task<long> findCurrentMark(HighWaterStatistics statistics, IManagedConnection conn, CancellationToken token)
        {
            // look for the current mark
            _gapDetector.Start = statistics.SafeStartMark;
            var current = await _runner.Query(_gapDetector, token);

            if (current.HasValue) return current.Value;

            // This happens when the agent is restarted with persisted
            // state, and has no previous current mark.
            if (statistics.CurrentMark == 0 && statistics.LastMark > 0)
            {
                return statistics.LastMark;
            }

            return statistics.CurrentMark;
        }

    }
}
