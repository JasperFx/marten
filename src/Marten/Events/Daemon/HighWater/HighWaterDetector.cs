using System;
using System.Collections.Concurrent;
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
    private readonly HighWaterStatisticsDetector _highWaterStatisticsDetector;
    private readonly ILogger _logger;
    private readonly ISingleQueryRunner _runner;
    private readonly MartenDatabase _database;
    private readonly EventGraph _graph;
    private readonly ProjectionOptions _settings;

    // #4867: per-tenant stale clocks for the vectorized high-water path. When a tenant's mark is
    // blocked by a gap immediately above it (an allocated-but-uncommitted — or rolled-back — seq_id),
    // this records WHEN the detector first observed that tenant stuck at that mark, so the gap can be
    // skipped once the tenant has been stale past StaleSequenceThreshold. This is the per-tenant
    // analogue of HighWaterAgent's in-memory `_current` timestamp on the store-global path: the state
    // is detector-scoped (one detector instance per running daemon) and resets on restart, which only
    // means a stale tenant waits one fresh threshold before skipping — never that events are lost.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tenantStaleSince = new();

    // #4953: bookkeeping for the store-global sequence gap the mark is currently stuck under. Records
    // WHEN the detector first saw the mark pinned at this position, plus the snapshot xmax and the
    // highest reserved sequence from that same statistics reading. The stale threshold is measured
    // from Since (never from mt_event_progression.last_updated, which is arbitrarily old on an idle
    // store), Xmax fences the liveness probe to transactions that could have reserved the gap, and
    // ReservedCeiling bounds how far a proven-dead skip may advance — sequence numbers reserved AFTER
    // the observation belong to newer transactions whose fate is not proven. Detector-scoped state:
    // resets on restart, which only means a stuck gap waits one fresh threshold before skipping.
    private sealed record StuckGapObservation(long Mark, DateTimeOffset Since, long Xmax, long ReservedCeiling);

    private StuckGapObservation? _stuckGap;

    public HighWaterDetector(MartenDatabase runner, EventGraph graph, ILogger logger)
    {
        _runner = runner;
        _database = runner;
        _graph = graph;
        _logger = logger;
        _highWaterStatisticsDetector = new HighWaterStatisticsDetector(graph);
        _settings = graph.Options.Projections;

        DatabaseIdentity = runner.Identifier;
        DatabaseUri = runner.Describe().DatabaseUri();
    }

    public Uri DatabaseUri { get; }

    /// <summary>
    /// #4596 Phase 2c — the SINGLE SWITCH that opts the running daemon into
    /// vectorized per-tenant high-water + per-tenant rebuilds. JasperFx Phase 2b
    /// wired both behaviors into <c>JasperFxAsyncDaemon</c> gated on this flag,
    /// so flipping it on lights up <c>TenantedHighWaterCoordinator</c> +
    /// per-tenant <c>RebuildProjectionAsync(name, tenantId, …)</c> without any
    /// further plumbing on the Marten side. When the user has not opted into
    /// <see cref="Marten.Events.IEventStoreOptions.UseTenantPartitionedEvents"/>
    /// this stays false → today's single store-global mark + single-shard
    /// rebuild, byte for byte.
    /// </summary>
    public bool SupportsTenantPartitioning => _graph.UseTenantPartitionedEvents;

    /// <summary>
    /// Advance the high water mark to the latest detected sequence
    /// </summary>
    /// <param name="token"></param>
    public async Task AdvanceHighWaterMarkToLatest(CancellationToken token)
    {
        // #4953: advance to the highest COMMITTED sequence, never the reserved last_value of
        // mt_events_sequence — reserved numbers can belong to transactions still in flight, and
        // marking past them permanently skips their events once they commit.
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);
        var committed = await _runner.Query(new CommittedSequenceHandler(_graph), token).ConfigureAwait(false);
        if (committed > statistics.LastMark)
        {
            await MarkHighWaterMarkInDatabaseAsync(committed, token).ConfigureAwait(false);
        }
    }

    internal class CommittedSequenceHandler: ISingleQueryHandler<long>
    {
        private readonly EventGraph _graph;

        public CommittedSequenceHandler(EventGraph graph)
        {
            _graph = graph;
        }

        public NpgsqlCommand BuildCommand()
        {
            return new NpgsqlCommand(
                $"select coalesce(max(seq_id), 0) from {_graph.DatabaseSchemaName}.mt_events");
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

    public string DatabaseIdentity { get; }

    public async Task<HighWaterStatistics> DetectInSafeZone(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);

        if (!_settings.UseTransactionEvidenceForGapSkipping)
        {
            return await detectInSafeZoneLegacy(statistics, token).ConfigureAwait(false);
        }

        // #4953: this method is reachable with NO staleness gating at all (rebuilds and catch-up call
        // it through HighWaterAgent.CheckNowAsync), and even the threshold-gated route cannot tell a
        // permanently dead sequence hole from one still held by an in-flight append. So: advance
        // normally when possible, and only skip a gap that (a) has been stuck past the stale threshold
        // measured from when THIS detector first observed it and (b) has no evidence of a live
        // reserving transaction — or the configured escape-hatch cap has expired.

        // Caught up / empty store: nothing to do
        if (statistics.LastMark == statistics.HighestSequence
            || statistics.HighestSequence == 0
            || (statistics.HighestSequence <= 1 && statistics.LastMark == 0))
        {
            statistics.CurrentMark = statistics.LastMark;
            _stuckGap = null;
            return statistics;
        }

        // First: the same gap-holding walk the Normal path uses. Contiguous committed progress is
        // taken as a plain advance — no skipping, no observability noise.
        var held = await runGapDetectorAsync(statistics.SafeStartMark, true, token).ConfigureAwait(false);
        if (held.HasValue && held.Value > statistics.LastMark)
        {
            statistics.CurrentMark = held.Value;
            _stuckGap = null;
            await persistDetectedMarkAsync(statistics, DetectionType.Normal, token).ConfigureAwait(false);
            return statistics;
        }

        // The mark is pinned under a gap. Start (or continue) the per-gap clock.
        statistics.CurrentMark = statistics.LastMark;
        trackStuckGap(statistics);
        var observed = _stuckGap;
        if (observed == null)
        {
            return statistics;
        }

        var age = statistics.Timestamp.Subtract(observed.Since);
        if (age < _settings.StaleSequenceThreshold)
        {
            // Give the gap a chance to fill in before even considering a skip
            return statistics;
        }

        var liveness = await _runner
            .Query(new GapLivenessProbe(_graph, observed.Since, observed.Xmax), token).ConfigureAwait(false);
        if (liveness.IndicatesLiveReserver)
        {
            var cap = _settings.SkipStaleGapsDespiteLiveTransactionsAfter;
            if (cap == null || age < cap.Value)
            {
                if (age > _settings.StaleSequenceThreshold * 5)
                {
                    _logger.LogWarning(
                        "Daemon high water detection has held before the sequence gap above {Mark} for {Age} because a transaction that may still fill it appears to be alive ({Liveness}). Projections will not advance until it commits, aborts, or the SkipStaleGapsDespiteLiveTransactionsAfter cap expires",
                        observed.Mark, age, liveness);
                }
                else
                {
                    _logger.LogInformation(
                        "Daemon high water detection is holding before the sequence gap above {Mark}: {Liveness}",
                        observed.Mark, liveness);
                }

                return statistics;
            }

            _logger.LogWarning(
                "Daemon high water detection is skipping the sequence gap above {Mark} DESPITE evidence of a live transaction ({Liveness}) because the gap has been stuck for {Age}, past the configured SkipStaleGapsDespiteLiveTransactionsAfter cap of {Cap}. Events committed later inside the skipped range will NOT be projected",
                observed.Mark, liveness, age, cap);
        }

        // Proven dead (or cap expired): walk past the gap, but never beyond the reserved ceiling
        // recorded when the gap was first observed — sequence numbers reserved after that belong to
        // newer transactions whose fate is not proven.
        var walk = await runGapDetectorAsync(statistics.SafeStartMark + 1, false, token).ConfigureAwait(false);
        var target = walk.HasValue ? Math.Min(walk.Value, observed.ReservedCeiling) : observed.ReservedCeiling;
        if (target <= statistics.LastMark)
        {
            return statistics;
        }

        _logger.LogWarning(
            "Daemon high water detection is skipping the event sequence range ({Mark}, {Target}] after the gap above {Mark} was stuck for {Age} with no evidence of a live transaction that could still fill it ({Liveness}). Any sequence numbers in that range that never committed were lost to rolled-back appends",
            observed.Mark, target, observed.Mark, age, liveness);

        statistics.SafeStartMark = target;
        statistics.CurrentMark = target;
        _stuckGap = null;
        await persistDetectedMarkAsync(statistics, DetectionType.SafeZoneSkipping, token).ConfigureAwait(false);

        return statistics;
    }

    // Pre-#4953 behavior, kept verbatim behind ProjectionOptions.UseTransactionEvidenceForGapSkipping = false
    private async Task<HighWaterStatistics> detectInSafeZoneLegacy(HighWaterStatistics statistics,
        CancellationToken token)
    {
        // Skip gap and find next safe sequence. #4964: the SafeZone path must NOT hold before a leading
        // gap — its whole purpose is to skip forward past a stuck (permanent) hole and record the skip.
        var safeSequence = await runGapDetectorAsync(statistics.SafeStartMark + 1, false, token).ConfigureAwait(false);
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

    // #4953: one GapDetector instance per call — Detect (the poll loop) and DetectInSafeZone
    // (rebuild/catch-up) can run concurrently, and shared mutable Start/Hold state between them was
    // a race. Retains the long-standing open-data-reader retry.
    private async Task<long?> runGapDetectorAsync(long start, bool holdBeforeLeadingGap, CancellationToken token)
    {
        var gapDetector = new GapDetector(_graph) { Start = start, HoldBeforeLeadingGap = holdBeforeLeadingGap };

        try
        {
            return await _runner.Query(gapDetector, token).ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            if (e.Message.Contains("An open data reader exists for this command"))
            {
                await Task.Delay(250.Milliseconds(), token).ConfigureAwait(false);
                return await _runner.Query(gapDetector, token).ConfigureAwait(false);
            }

            throw;
        }
    }

    // #4953: per-gap observation bookkeeping — see StuckGapObservation
    private void trackStuckGap(HighWaterStatistics statistics)
    {
        if (statistics.CurrentMark >= statistics.HighestSequence || statistics.CurrentMark > statistics.LastMark)
        {
            // caught up, or the mark advanced — whatever gap existed before is resolved
            _stuckGap = null;
            return;
        }

        if (statistics.CurrentMark == 0 && statistics.HighestSequence <= 1)
        {
            // pristine store (Postgres sequences report last_value = 1 before first use)
            _stuckGap = null;
            return;
        }

        var current = _stuckGap;
        if (current == null || current.Mark != statistics.CurrentMark)
        {
            _stuckGap = new StuckGapObservation(
                statistics.CurrentMark,
                statistics.Timestamp,
                (statistics as MartenHighWaterStatistics)?.CurrentXmax ?? 0,
                statistics.HighestSequence);
        }
    }


    public async Task<HighWaterStatistics> Detect(CancellationToken token)
    {
        var statistics = await loadCurrentStatistics(token).ConfigureAwait(false);

        await calculateHighWaterMark(statistics, DetectionType.Normal, token).ConfigureAwait(false);

        // #4953: the poll loop is where a stuck gap is usually seen first — record it here so the
        // per-gap stale clock starts as early as possible
        trackStuckGap(statistics);

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
    /// shape; per-tenant gap detection + stale-threshold skipping are internal to
    /// <see cref="loadPerTenantStatistics"/> (#4867), so both variants share one
    /// implementation. When the flag is off, inherit the default interface impl
    /// (store-global safe-zone reading).
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

        // The vectorized poll is identical to DetectForTenantsAsync: per-tenant
        // advancement, gap holding, and threshold-gated gap skipping all live
        // inside loadPerTenantStatistics because the JasperFx coordinator drives
        // only the normal poll (there is no daemon-level per-tenant safe-zone
        // scheduling to defer to).
        var stats = await loadPerTenantStatistics(tenantIds, token).ConfigureAwait(false);
        return new HighWaterVector(stats);
    }

    private async Task<IReadOnlyList<HighWaterStatistics>> loadPerTenantStatistics(
        IReadOnlyCollection<string> tenantIds, CancellationToken token)
    {
        var schema = _graph.DatabaseSchemaName;
        // #4681: the per-tenant high-water identity prefix is produced by
        // HighWaterShardIdentity rather than hand-rolled here, so any future
        // change to the high-water grammar (separator, version segment, escape
        // rule) only needs to land in one place.
        var highWaterPrefix = HighWaterShardIdentity.PerTenantPrefix;

        // This method only runs under per-tenant event partitioning (the caller collapses to the
        // store-global reading when the flag is off), so read each tenant's height from max(seq_id) of its
        // own mt_events partition — NOT from the per-tenant sequence. Same reasoning as the store-global
        // #4712 fix in HighWaterStatisticsDetector: under partitioning the per-tenant sequence
        // (mt_events_sequence_{suffix}) is left at its initial value (last_value=1, is_called=false) for
        // events appended via the shared sequence or reassigned by BulkInsertEventsAsync, so reading it
        // reports a high-water of 0 for a fully-populated tenant and its projections never start. max(seq_id)
        // is the authoritative committed height regardless of how the events were inserted, so a tenant with
        // no persisted progression row yet still gets its true high-water and its per-tenant agent advances.
        // The LEFT JOINs preserve a row per input tenant even when its partition / progression row does not
        // exist yet (returns null → treated as zero downstream).
        //
        // #4867: the `walk` lateral is the per-tenant analogue of the store-global GapDetector — the
        // highest committed seq_id reachable from the persisted mark without crossing a gap. Without it,
        // CurrentMark was seeded straight from the persisted progression row, so a tenant's mark froze
        // forever after the first persisted HighWaterMark:{tenant} row and second batches never projected.
        // The walk is only computed when there is a persisted mark with committed events above it
        // (the gates on prog.last_seq_id / ev.max_seq_id), so caught-up tenants stay cheap.
        var sql = $@"
with inputs(tenant_id) as (select unnest(:tenants))
select
    i.tenant_id,
    coalesce(ev.max_seq_id, 0)         as last_value,
    coalesce(prog.last_seq_id, 0)      as last_seq_id,
    prog.last_updated                  as last_updated,
    transaction_timestamp()            as ""timestamp"",
    walk.current_bound                 as current_bound
from inputs i
left join lateral (
    select max(e.seq_id) as max_seq_id
    from {schema}.mt_events e
    where e.tenant_id = i.tenant_id
) ev on true
left join {schema}.mt_event_progression prog
    on prog.name = :prefix || i.tenant_id
left join lateral (
    select coalesce(
        (select g.seq_id
         from (select e.seq_id,
                      lead(e.seq_id) over (order by e.seq_id) as next_seq
               from {schema}.mt_events e
               where e.tenant_id = i.tenant_id
                 and e.seq_id >= prog.last_seq_id) g
         where g.next_seq is not null
           and g.next_seq - g.seq_id > 1
         order by g.seq_id
         limit 1),
        ev.max_seq_id
    ) as current_bound
    where coalesce(prog.last_seq_id, 0) > 0
      and coalesce(ev.max_seq_id, 0) > prog.last_seq_id
) walk on true;";

        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            var results = new List<HighWaterStatistics>(tenantIds.Count);
            var staleTenants = new List<HighWaterStatistics>();

            await using (var cmd = conn.CreateCommand(sql)
                             .With("tenants", tenantIds.ToArray())
                             .With("prefix", highWaterPrefix))
            {
                await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);

                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var tenantId = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                    var lastValue = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
                    var lastSeqId = await reader.GetFieldValueAsync<long>(2, token).ConfigureAwait(false);

                    DateTimeOffset? lastUpdated = await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false);
                    var timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(4, token).ConfigureAwait(false);
                    long? currentBound = await reader.IsDBNullAsync(5, token).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<long>(5, token).ConfigureAwait(false);

                    var statistics = new HighWaterStatistics
                    {
                        TenantId = tenantId,
                        HighestSequence = lastValue,
                        LastMark = lastSeqId,
                        LastUpdated = lastUpdated,
                        Timestamp = timestamp
                    };

                    // CurrentMark = the latest sequence the daemon may treat as "caught up".
                    // SafeStartMark mirrors CurrentMark — a per-tenant walker resumes from the
                    // last good mark.
                    if (lastSeqId == 0)
                    {
                        // No persisted progression row yet — seed from the tenant's committed
                        // max(seq_id), exactly the pre-#4867 first-sighting behavior.
                        statistics.CurrentMark = statistics.SafeStartMark = lastValue;
                        _tenantStaleSince.TryRemove(tenantId, out _);
                    }
                    else if (lastValue <= lastSeqId)
                    {
                        // Caught up — and never rewind a persisted mark, even if committed
                        // events were archived out from under it.
                        statistics.CurrentMark = statistics.SafeStartMark = lastSeqId;
                        _tenantStaleSince.TryRemove(tenantId, out _);
                    }
                    else if (currentBound > lastSeqId)
                    {
                        // #4867 THE fix: committed events reach contiguously above the persisted
                        // mark, so advance immediately — this is what un-freezes a tenant's second
                        // batch. The walk stops before any gap, so an allocated-but-uncommitted
                        // lower seq_id is never silently skipped.
                        statistics.CurrentMark = statistics.SafeStartMark = currentBound.Value;
                        _tenantStaleSince.TryRemove(tenantId, out _);
                    }
                    else
                    {
                        // A gap sits immediately above the persisted mark (in-flight append or a
                        // rolled-back sequence number). Hold the mark and let the stale clock run;
                        // once the tenant has been stuck past StaleSequenceThreshold, skip the gap
                        // below — the per-tenant mirror of the store-global DetectInSafeZone wait.
                        statistics.CurrentMark = statistics.SafeStartMark = lastSeqId;
                        var staleSince = _tenantStaleSince.GetOrAdd(tenantId, timestamp);
                        if (timestamp.Subtract(staleSince) > _settings.StaleSequenceThreshold)
                        {
                            staleTenants.Add(statistics);
                        }
                    }

                    results.Add(statistics);
                }
            }

            foreach (var statistics in staleTenants)
            {
                await trySkipStaleTenantGapAsync(conn, statistics, token).ConfigureAwait(false);
            }

            return results;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    // #4867: safe-harbor skip for one tenant that has been stale past StaleSequenceThreshold. Walks the
    // tenant's committed events from just above the stuck mark (the store-global equivalent is
    // DetectInSafeZone's `_gapDetector.Start = SafeStartMark + 1`) and lands on the highest seq_id
    // reachable without crossing ANOTHER gap, so multiple gaps are skipped one threshold at a time —
    // the same iterative cadence as the store-global agent.
    private async Task trySkipStaleTenantGapAsync(NpgsqlConnection conn, HighWaterStatistics statistics,
        CancellationToken token)
    {
        var sql = $@"
select coalesce(
    (select g.seq_id
     from (select e.seq_id,
                  lead(e.seq_id) over (order by e.seq_id) as next_seq
           from {_graph.DatabaseSchemaName}.mt_events e
           where e.tenant_id = :tenant
             and e.seq_id > :mark) g
     where g.next_seq is not null
       and g.next_seq - g.seq_id > 1
     order by g.seq_id
     limit 1),
    (select max(e.seq_id)
     from {_graph.DatabaseSchemaName}.mt_events e
     where e.tenant_id = :tenant
       and e.seq_id > :mark));";

        await using var cmd = conn.CreateCommand(sql)
            .With("tenant", statistics.TenantId!)
            .With("mark", statistics.LastMark);

        var raw = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (raw is not long safeSequence || safeSequence <= statistics.LastMark)
        {
            return;
        }

        _logger.LogInformation(
            "Daemon projection high water detection for tenant {TenantId} skipping a gap in the event sequence after being stale past the {Threshold} threshold, determined that the 'safe harbor' sequence is at {SafeHarborSequence}",
            statistics.TenantId, _settings.StaleSequenceThreshold, safeSequence);

        statistics.CurrentMark = statistics.SafeStartMark = safeSequence;
        statistics.IncludesSkipping = true;
        _tenantStaleSince.TryRemove(statistics.TenantId!, out _);
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
            statistics.CurrentMark = await findCurrentMark(statistics, detectionType, token).ConfigureAwait(false);
        }

        await persistDetectedMarkAsync(statistics, detectionType, token).ConfigureAwait(false);
    }

    private async Task persistDetectedMarkAsync(HighWaterStatistics statistics, DetectionType detectionType,
        CancellationToken token)
    {
        if (!statistics.HasChanged)
        {
            return;
        }

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

    public Task MarkHighWaterMarkInDatabaseAsync(long currentMark, CancellationToken token)
    {
        return _runner.Query(new MarkHighWaterQueryHandler(_graph, currentMark), token);
    }

    // #4717: persist a durable per-tenant high-water row (HighWaterMark:<tenant>) so each tenant's mark
    // survives a daemon restart. Under per-tenant event partitioning the store-global mt_events_sequence
    // is never advanced and a single HighWaterMark row cannot represent multiple tenants. Invoked by
    // JasperFx's TenantedHighWaterCoordinator on each vectorized per-tenant poll.
    public Task MarkHighWaterForTenantAsync(string tenantId, long sequence, CancellationToken token)
    {
        return _runner.Query(new MarkTenantHighWaterQueryHandler(_graph, tenantId, sequence), token);
    }

    public class MarkTenantHighWaterQueryHandler: ISingleQueryHandler<bool>
    {
        private readonly EventGraph _graph;
        private readonly string _tenantId;
        private readonly long _currentMark;

        public MarkTenantHighWaterQueryHandler(EventGraph graph, string tenantId, long currentMark)
        {
            _graph = graph;
            _tenantId = tenantId;
            _currentMark = currentMark;
        }

        public NpgsqlCommand BuildCommand()
        {
            return new NpgsqlCommand(
                    $"select {_graph.DatabaseSchemaName}.mt_mark_event_progression(:name, :seq);")
                .With("name", HighWaterShardIdentity.PerTenant(_tenantId))
                .With("seq", _currentMark);
        }

        public Task<bool> HandleAsync(DbDataReader reader, CancellationToken token)
        {
            return Task.FromResult(true);
        }
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
                    $"select {_graph.DatabaseSchemaName}.mt_mark_event_progression('{HighWaterShardIdentity.StoreGlobal}', :seq);")
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
                    $"select {graph.DatabaseSchemaName}.mt_mark_progression_with_skip('{HighWaterShardIdentity.StoreGlobal}', :ending, :starting);")
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

    private async Task<long> findCurrentMark(HighWaterStatistics statistics, DetectionType detectionType,
        CancellationToken token)
    {
        // #4964: only the Normal path holds before a leading gap. The SafeZone path deliberately skips
        // forward past a permanent hole (and records the skip), so it must keep the old skip-to-max behavior.
        var current = await runGapDetectorAsync(statistics.SafeStartMark,
            detectionType == DetectionType.Normal, token).ConfigureAwait(false);

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
