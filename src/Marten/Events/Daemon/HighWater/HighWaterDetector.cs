using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon.HighWater;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.HighWater;

internal class HighWaterDetector: IHighWaterDetector
{
    private readonly GapDetector _gapDetector;
    private readonly HighWaterStatisticsDetector _highWaterStatisticsDetector;
    private readonly ILogger _logger;
    private readonly ISingleQueryRunner _runner;
    private readonly EventGraph _graph;
    private readonly ProjectionOptions _settings;

    public HighWaterDetector(MartenDatabase runner, EventGraph graph, ILogger logger)
    {
        _runner = runner;
        _graph = graph;
        _logger = logger;
        _gapDetector = new GapDetector(graph);
        _highWaterStatisticsDetector = new HighWaterStatisticsDetector(graph);
        _settings = graph.Options.Projections;

        DatabaseName = runner.Identifier;
    }

    /// <summary>
    /// Advance the high water mark to the latest detected sequence
    /// </summary>
    /// <param name="token"></param>
    public async Task AdvanceHighWaterMarkToLatest(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);
        await markHighWaterMarkInDatabaseAsync(token, statistics.HighestSequence).ConfigureAwait(false);
    }

    public string DatabaseName { get; }

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
            var currentMark = statistics.CurrentMark;
            await markHighWaterMarkInDatabaseAsync(token, currentMark).ConfigureAwait(false);

            if (!statistics.LastUpdated.HasValue)
            {
                var current = await loadCurrentStatistics(token).ConfigureAwait(false);
                statistics.LastUpdated = current.LastUpdated;
            }
        }
    }

    private async Task markHighWaterMarkInDatabaseAsync(CancellationToken token, long currentMark)
    {
        await using var cmd =
            new NpgsqlCommand(
                $"select {_graph.DatabaseSchemaName}.mt_mark_event_progression('{ShardState.HighWaterMark}', :seq);")
                .With("seq", currentMark);

        await _runner.SingleCommit(cmd, token).ConfigureAwait(false);
    }

    private async Task<HighWaterStatistics> loadCurrentStatistics(CancellationToken token)
    {
        return await _runner.Query(_highWaterStatisticsDetector, token).ConfigureAwait(false);
    }

    private async Task<long> findCurrentMark(HighWaterStatistics statistics, CancellationToken token)
    {
        // look for the current mark
        _gapDetector.Start = statistics.SafeStartMark;
        long? current;
        try
        {
            current = await _runner.Query(_gapDetector, token).ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            if (e.Message.Contains("An open data reader exists for this command"))
            {
                await Task.Delay(250.Milliseconds(), token).ConfigureAwait(false);
                current = await _runner.Query(_gapDetector, token).ConfigureAwait(false);
            }
            else
            {
                throw;
            }
        }

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
