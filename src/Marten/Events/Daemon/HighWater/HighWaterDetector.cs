using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
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
    private readonly MartenDatabase _database;
    private readonly EventGraph _graph;
    private readonly ProjectionOptions _settings;

    public HighWaterDetector(MartenDatabase runner, EventGraph graph, ILogger logger)
    {
        _runner = runner;
        _database = runner;
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

    /// <summary>
    /// #4596 Phase 2: vectorized per-tenant high-water polling. When
    /// <see cref="Marten.Events.EventGraph.UseTenantPartitionedEvents"/> is on,
    /// poll every supplied tenant's per-tenant sequence + per-tenant high-water
    /// progression row in a single round-trip and return one
    /// <see cref="HighWaterStatistics"/> per tenant with <c>TenantId</c>
    /// populated. When the flag is off, inherit the default interface impl
    /// (single store-global reading) so existing single-mark consumers keep
    /// compiling and behaving unchanged.
    /// </summary>
    public async Task<HighWaterVector> DetectForTenantsAsync(
        IReadOnlyCollection<string> tenantIds, CancellationToken token)
    {
        if (!_graph.UseTenantPartitionedEvents)
        {
            // Flag off → no per-tenant partitioning; collapse to store-global.
            var global = await Detect(token).ConfigureAwait(false);
            return HighWaterVector.ForGlobal(global);
        }

        if (tenantIds.Count == 0)
        {
            return new HighWaterVector([]);
        }

        var stats = await loadPerTenantStatistics(tenantIds, token).ConfigureAwait(false);
        return new HighWaterVector(stats);
    }

    /// <summary>
    /// Safe-zone variant of <see cref="DetectForTenantsAsync"/>. Same vectorized
    /// shape; per-tenant independent gap detection is the caller's job
    /// (<see cref="VectorizedHighWaterMonitor"/>). When the flag is off, inherit
    /// the default interface impl (store-global safe-zone reading).
    /// </summary>
    public async Task<HighWaterVector> DetectInSafeZoneForTenantsAsync(
        IReadOnlyCollection<string> tenantIds, CancellationToken token)
    {
        if (!_graph.UseTenantPartitionedEvents)
        {
            var global = await DetectInSafeZone(token).ConfigureAwait(false);
            return HighWaterVector.ForGlobal(global);
        }

        if (tenantIds.Count == 0)
        {
            return new HighWaterVector([]);
        }

        // The vectorized poll itself is the same; the daemon-level safe-zone
        // behavior (skipping gaps for stale tenants) is layered on top by
        // VectorizedHighWaterMonitor — store responsibility is just one
        // round-trip per poll regardless of normal vs safe-zone mode.
        var stats = await loadPerTenantStatistics(tenantIds, token).ConfigureAwait(false);
        return new HighWaterVector(stats);
    }

    private async Task<IReadOnlyList<HighWaterStatistics>> loadPerTenantStatistics(
        IReadOnlyCollection<string> tenantIds, CancellationToken token)
    {
        var tenantsTable = _graph.Options.TenantPartitions!.TenantsTableName;
        var schema = _graph.DatabaseSchemaName;
        var highWaterPrefix = ShardState.HighWaterMark + ":";

        // Single round-trip: for each requested tenant, join (mt_tenant_partitions
        // → pg_sequences for that partition's mt_events_sequence_{suffix} →
        // mt_event_progression for the per-tenant high-water row keyed by
        // `'{HighWaterMark}:{tenantId}'`, matching Session 3's per-tenant naming
        // convention for the high-water shard). LEFT JOINs preserve a row per
        // input tenant even when the partition / sequence / progression row
        // hasn't been created yet (returns null → treated as zero downstream).
        var sql = $@"
with inputs(tenant_id) as (select unnest(:tenants))
select
    i.tenant_id,
    coalesce(seq.last_value, 0)        as last_value,
    coalesce(prog.last_seq_id, 0)      as last_seq_id,
    prog.last_updated                  as last_updated,
    transaction_timestamp()            as ""timestamp""
from inputs i
left join {tenantsTable} p
    on p.partition_value = i.tenant_id
left join pg_sequences seq
    on seq.schemaname = '{schema}'
    and seq.sequencename = 'mt_events_sequence_' || p.partition_suffix
left join {schema}.mt_event_progression prog
    on prog.name = :prefix || i.tenant_id;";

        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand(sql)
                .With("tenants", tenantIds.ToArray())
                .With("prefix", highWaterPrefix);
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);

            var results = new List<HighWaterStatistics>(tenantIds.Count);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var tenantId = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                var lastValue = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
                var lastSeqId = await reader.GetFieldValueAsync<long>(2, token).ConfigureAwait(false);

                DateTimeOffset? lastUpdated = await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false);
                var timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(4, token).ConfigureAwait(false);

                // CurrentMark = the latest sequence the daemon may treat as
                // "caught up". With per-tenant gap detection not yet wired in
                // (a Phase 3 refinement), seed CurrentMark from the saved
                // progression row when one exists, otherwise from the
                // tenant's sequence last_value. SafeStartMark mirrors
                // CurrentMark for the same reason — the safe-zone walker
                // resumes from the last good mark.
                var currentMark = lastSeqId > 0 ? lastSeqId : lastValue;
                results.Add(new HighWaterStatistics
                {
                    TenantId = tenantId,
                    HighestSequence = lastValue,
                    LastMark = lastSeqId,
                    SafeStartMark = currentMark,
                    CurrentMark = currentMark,
                    LastUpdated = lastUpdated,
                    Timestamp = timestamp
                });
            }

            return results;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
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
