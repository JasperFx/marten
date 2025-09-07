using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon.HighWater;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.HighWater;

internal enum DetectionType
{
    Normal,
    SafeZoneSkipping
}

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

        DatabaseIdentity = runner.Identifier;
        DatabaseUri = runner.Describe().DatabaseUri();
    }

    public Uri DatabaseUri { get; }

    /// <summary>
    /// Advance the high water mark to the latest detected sequence
    /// </summary>
    /// <param name="token"></param>
    public async Task AdvanceHighWaterMarkToLatest(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);
        await MarkHighWaterMarkInDatabaseAsync(statistics.HighestSequence, token).ConfigureAwait(false);
    }

    public string DatabaseIdentity { get; }

    public async Task<HighWaterStatistics> DetectInSafeZone(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);

        // Skip gap and find next safe sequence
        _gapDetector.Start = statistics.SafeStartMark + 1;

        var safeSequence = await _runner.Query(_gapDetector, token).ConfigureAwait(false);
        if (safeSequence.HasValue)
        {
            _logger.LogInformation(
                "Daemon projection high water detection skipping a gap in event sequence, determined that the 'safe harbor' sequence is at {SafeHarborSequence}",
                safeSequence);
        }
        else
        {
            _logger.LogInformation("Daemon projection high water detection was not able to determine a safe harbor sequence, will try again soon");
        }

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
                // This has to take into account the 32 problem. If the
                // HighestSequence is less than 32 higher than the LastMark or CurrentMark, do NOT advance
                // https://github.com/JasperFx/marten/issues/3865
                var safeSequenceNumber = statistics.HighestSequence - 32;
                if (safeSequenceNumber > statistics.LastMark)
                {
                    statistics.SafeStartMark = safeSequenceNumber;
                    statistics.CurrentMark = safeSequenceNumber;
                }
            }
        }

        await calculateHighWaterMark(statistics, DetectionType.SafeZoneSkipping, token).ConfigureAwait(false);

        return statistics;
    }


    public async Task<HighWaterStatistics> Detect(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);

        await calculateHighWaterMark(statistics, DetectionType.Normal, token).ConfigureAwait(false);

        return statistics;
    }

    private async Task calculateHighWaterMark(HighWaterStatistics statistics, DetectionType detectionType,
        CancellationToken token)
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

            if (detectionType == DetectionType.SafeZoneSkipping)
            {
                if (_graph.EnableAdvancedAsyncTracking)
                {
                    var actual = await TryMarkHighWaterSkippingAsync(currentMark, statistics.LastMark, token).ConfigureAwait(false);
                    if (actual == currentMark)
                    {
                        statistics.IncludesSkipping = true;
                    }
                    else
                    {
                        statistics.CurrentMark = actual;
                        statistics.IncludesSkipping = false;
                    }
                }
                else
                {
                    statistics.IncludesSkipping = true;
                    await MarkHighWaterMarkInDatabaseAsync(currentMark, token).ConfigureAwait(false);
                }
            }
            else
            {
                await MarkHighWaterMarkInDatabaseAsync(currentMark, token).ConfigureAwait(false);
            }

            if (!statistics.LastUpdated.HasValue)
            {
                var current = await loadCurrentStatistics(token).ConfigureAwait(false);
                statistics.LastUpdated = current.LastUpdated;
            }
        }
    }

    public Task MarkHighWaterMarkInDatabaseAsync(long currentMark, CancellationToken token)
    {
        return _runner.Query(new MarkHighWaterQueryHandler(_graph, currentMark), token);
    }

    public class MarkHighWaterQueryHandler: ISingleQueryHandler<bool>
    {
        private readonly EventGraph _graph;
        private readonly long _currentMark;

        public MarkHighWaterQueryHandler(EventGraph graph, long currentMark)
        {
            _graph = graph;
            _currentMark = currentMark;
        }

        public NpgsqlCommand BuildCommand()
        {
            return new NpgsqlCommand(
                    $"select {_graph.DatabaseSchemaName}.mt_mark_event_progression('{ShardState.HighWaterMark}', :seq);")
                .With("seq", _currentMark);
        }

        public Task<bool> HandleAsync(DbDataReader reader, CancellationToken token)
        {
            return Task.FromResult(true);
        }
    }

    public Task<long> TryMarkHighWaterSkippingAsync(long endingMark, long currentMark, CancellationToken token)
    {
        if (endingMark <= currentMark)
            throw new ArgumentOutOfRangeException(nameof(endingMark),
                "Ending sequence should be greater than the current mark");

        return _runner.Query(new TryMarkHighWaterSkippingHandler(_graph, endingMark, currentMark), token);
    }

    public class TryMarkHighWaterSkippingHandler: ISingleQueryHandler<long>
    {
        private readonly NpgsqlCommand _command;

        public TryMarkHighWaterSkippingHandler(EventGraph graph, long endingMark, long currentMark)
        {
            _command = new NpgsqlCommand(
                    $"select {graph.DatabaseSchemaName}.mt_mark_progression_with_skip('{ShardState.HighWaterMark}', :ending, :starting);")
                .With("ending", endingMark)
                .With("starting", currentMark)
                ;

        }

        public NpgsqlCommand BuildCommand()
        {
            return _command;
        }

        public async Task<long> HandleAsync(DbDataReader reader, CancellationToken token)
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            return 0;
        }
    }

    public async Task TryCorrectProgressInDatabaseAsync(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);
        if (statistics.LastMark > statistics.HighestSequence)
        {
            await using var cmd =
                new NpgsqlCommand(
                        $"update {_graph.DatabaseSchemaName}.mt_event_progression set last_seq_id = :seq, last_updated = transaction_timestamp()")
                    .With("seq", statistics.HighestSequence);

            await _runner.SingleCommit(cmd, token).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<HighWaterDetectionSkip>> FetchLastProgressionSkipsAsync(int limit, CancellationToken cancellationToken)
    {
        return _runner.Query(new EventProgressionSkipsHandler(_graph, limit), cancellationToken);
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
        if (statistics is { CurrentMark: 0, LastMark: > 0 })
        {
            return statistics.LastMark;
        }

        return statistics.CurrentMark;
    }
}
