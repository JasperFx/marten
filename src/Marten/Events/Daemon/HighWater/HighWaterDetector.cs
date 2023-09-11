using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.HighWater;

internal class HighWaterDetector: IHighWaterDetector
{
    private readonly GapDetector _gapDetector;
    private readonly HighWaterStatisticsDetector _highWaterStatisticsDetector;
    private readonly ILogger _logger;
    private readonly NpgsqlParameter _newSeq;
    private readonly ISingleQueryRunner _runner;
    private readonly NpgsqlCommand _updateStatus;
    private readonly ProjectionOptions _settings;

    public HighWaterDetector(ISingleQueryRunner runner, EventGraph graph, ILogger logger)
    {
        _runner = runner;
        _logger = logger;
        _gapDetector = new GapDetector(graph);
        _highWaterStatisticsDetector = new HighWaterStatisticsDetector(graph);

        _updateStatus =
            new NpgsqlCommand(
                $"select {graph.DatabaseSchemaName}.mt_mark_event_progression('{ShardState.HighWaterMark}', :seq);");
        _newSeq = _updateStatus.AddNamedParameter("seq", 0L);

        _settings = graph.Options.Projections;
    }

    public async Task<HighWaterStatistics> DetectInSafeZone(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);

        // Skip gap and find next safe sequence
        _gapDetector.Start = statistics.SafeStartMark + 1;

        var safeSequence = await _runner.Query(_gapDetector, token).ConfigureAwait(false);
        _logger.LogInformation(
            "Daemon projection high water detection skipping a gap in event sequence, determined that the 'safe harbor' sequence is at {SafeHarborSequence}",
            safeSequence);

        if (safeSequence.HasValue)
        {
            statistics.SafeStartMark = safeSequence.Value;
        }
        else if (statistics.TryGetStaleAge(out var time))
        {
            // This is for GH-2681. What if there's a gap
            // from the last good spot and the latest sequence?
            // Instead of doing this in an infinite loop, advance
            // the sequence
            if (time > _settings.StaleSequenceThreshold)
            {
                statistics.SafeStartMark = statistics.HighestSequence;
                statistics.CurrentMark = statistics.HighestSequence;
            }
        }

        await calculateHighWaterMark(statistics, token).ConfigureAwait(false);

        return statistics;
    }


    public async Task<HighWaterStatistics> Detect(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);

        await calculateHighWaterMark(statistics, token).ConfigureAwait(false);

        return statistics;
    }

    private async Task calculateHighWaterMark(HighWaterStatistics statistics, CancellationToken token)
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
            statistics.CurrentMark = await findCurrentMark(statistics, token).ConfigureAwait(false);
        }

        if (statistics.HasChanged)
        {
            _newSeq.Value = statistics.CurrentMark;
            await _runner.SingleCommit(_updateStatus, token).ConfigureAwait(false);

            if (!statistics.LastUpdated.HasValue)
            {
                var current = await loadCurrentStatistics(token).ConfigureAwait(false);
                statistics.LastUpdated = current.LastUpdated;
            }
        }
    }

    private async Task<HighWaterStatistics> loadCurrentStatistics(CancellationToken token)
    {
        return await _runner.Query(_highWaterStatisticsDetector, token).ConfigureAwait(false);
    }

    private async Task<long> findCurrentMark(HighWaterStatistics statistics, CancellationToken token)
    {
        // look for the current mark
        _gapDetector.Start = statistics.SafeStartMark;
        var current = await _runner.Query(_gapDetector, token).ConfigureAwait(false);

        if (current.HasValue)
        {
            return current.Value;
        }

        // This happens when the agent is restarted with persisted
        // state, and has no previous current mark.
        if (statistics.CurrentMark == 0 && statistics.LastMark > 0)
        {
            return statistics.LastMark;
        }

        return statistics.CurrentMark;
    }
}
