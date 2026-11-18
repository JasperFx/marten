#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Schema;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.HighWater;

/// <summary>
///     #6007: a skip of the high water mark is provisional for <see cref="Projections.ProjectionOptions.LateEventDetectionWindow" />.
///     Every skip records the sequence numbers inside its range that had no committed event at the time
///     (<c>mt_high_water_skips.gaps</c>). This checks those numbers on the high water poll, at most once per
///     <see cref="Projections.ProjectionOptions.LateEventProbeInterval" />, and for each event that has since
///     committed in one of them:
///     <list type="bullet">
///         <item>records it in <c>mt_high_water_late_events</c> (exactly once, whichever node finds it first),</item>
///         <item>counts it in the <c>marten.daemon.late_events</c> metric and logs a warning, or an error when the range
///         was skipped by proof that nothing could fill it, because that is a bug in high water detection,</item>
///         <item>and rebuilds the event's stream in every async single-stream projection that handles it, through the
///         per-identity rebuild of the shard running in this process (jasperfx#1201).</item>
///     </list>
///     Other projections and subscriptions are not repaired: they cannot safely take an event out of order.
/// </summary>
internal sealed class LateEventRecovery
{
    private const int PendingRepairBatchSize = 500;

    private readonly MartenDatabase _database;
    private readonly EventGraph _graph;
    private readonly ILogger _logger;
    private readonly object _repairLock = new();

    private long _nextProbeTicks;
    private Task _repairs = Task.CompletedTask;

    public LateEventRecovery(MartenDatabase database, EventGraph graph, ILogger logger)
    {
        _database = database;
        _graph = graph;
        _logger = logger;
    }

    /// <summary>
    ///     Called on every high water poll. Costs a clock read unless a probe is due, and the probe itself is three
    ///     statements against two tables that are empty on a store that never skipped.
    /// </summary>
    public async Task ProbeIfDueAsync(CancellationToken token)
    {
        if (!_graph.EnableAdvancedAsyncTracking) return;

        var now = DateTimeOffset.UtcNow.UtcTicks;
        var due = Interlocked.Read(ref _nextProbeTicks);
        if (now < due) return;

        var next = now + _graph.Options.Projections.LateEventProbeInterval.Ticks;
        if (Interlocked.CompareExchange(ref _nextProbeTicks, next, due) != due) return;

        try
        {
            await ProbeAsync(token).ConfigureAwait(false);
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            // Never fail a high water poll over this
            _logger.LogError(e, "Unable to check the skipped high water ranges of database {Database} for late events",
                _database.Identifier);
        }
    }

    internal async Task ProbeAsync(CancellationToken token)
    {
        var schema = _graph.DatabaseSchemaName;
        var skips = $"{schema}.{EventProgressionSkippingTable.Name}";
        var ledger = $"{schema}.{LateEventsTable.Name}";

        // 1. Record every event that has committed inside a recorded gap of an open skip. The insert's conflict
        //    rule is what makes each late event reported once, by whichever node finds it first.
        // 2. Close the skips that are past the window, after the last look in (1). A skip without gaps is either
        //    from before 10.0 or had nothing missing, so there is nothing to look at.
        // 3. The late events that one of this store's async single-stream projections has not taken in yet. Each
        //    store tracks its own projections, so two applications sharing the schema do not settle each other's.
        // Under per-tenant partitioned events every tenant draws its own sequence and only per-tenant marks move
        // projections (#4913), so a store-global skip says nothing about any one tenant's events
        var tenantMatch = _graph.UseTenantPartitionedEvents
            ? "e.tenant_id = s.tenant_id"
            : $"(s.tenant_id = '{EventProgressionSkippingTable.GlobalTenant}' or e.tenant_id = s.tenant_id)";

        var sql = $@"
insert into {ledger} (seq_id, tenant_id, stream_id, event_type, skip_starting_sequence, skip_ending_sequence, skip_reason)
select e.seq_id, e.tenant_id, e.stream_id::varchar, e.type, s.starting_sequence, s.ending_sequence, s.reason
from {skips} s
cross join lateral generate_series(1, coalesce(cardinality(s.gaps), 0) - 1, 2) as i(n)
join {schema}.mt_events e
  on e.seq_id >= s.gaps[i.n] and e.seq_id <= s.gaps[i.n + 1]
 and {tenantMatch}
where s.closed_at is null
on conflict do nothing
returning seq_id, tenant_id, stream_id, event_type, skip_starting_sequence, skip_ending_sequence, skip_reason;

update {skips} set closed_at = transaction_timestamp()
where closed_at is null
  and (gaps is null or cardinality(gaps) < 2 or timestamp < transaction_timestamp() - :window);

select seq_id, tenant_id, stream_id, event_type, repaired_projections
from {ledger}
where cardinality(:repairs) > 0 and not (coalesce(repaired_projections, '{{}}') @> :repairs)
order by seq_id
limit {PendingRepairBatchSize};";

        var late = new List<LateEvent>();
        var pending = new List<PendingRepair>();
        var repairable = repairableProjectionNames();

        await using (var conn = _database.CreateConnection())
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand(sql);
            cmd.Parameters.Add(new NpgsqlParameter("repairs", NpgsqlDbType.Array | NpgsqlDbType.Varchar)
            {
                Value = repairable
            });
            cmd.Parameters.Add(new NpgsqlParameter("window", NpgsqlDbType.Interval)
            {
                Value = _graph.Options.Projections.LateEventDetectionWindow
            });

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                late.Add(new LateEvent(
                    await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<string>(3, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<long>(4, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<long>(5, token).ConfigureAwait(false),
                    await reader.IsDBNullAsync(6, token).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<string>(6, token).ConfigureAwait(false)));
            }

            // The update has no result set
            await reader.NextResultAsync(token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                pending.Add(new PendingRepair(
                    await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<string>(2, token).ConfigureAwait(false),
                    await reader.GetFieldValueAsync<string>(3, token).ConfigureAwait(false),
                    await reader.IsDBNullAsync(4, token).ConfigureAwait(false)
                        ? []
                        : await reader.GetFieldValueAsync<string[]>(4, token).ConfigureAwait(false)));
            }
        }

        // What this store still owes each event
        foreach (var repair in pending)
        {
            repair.Owed = repairable.Except(repair.Repaired).ToArray();
        }

        foreach (var @event in late)
        {
            report(@event);
        }

        if (pending.Count > 0)
        {
            startRepairs(pending, token);
        }
    }

    private void report(LateEvent late)
    {
        var reason = late.Reason ?? "Unknown";
        _graph.Options.OpenTelemetry.RecordLateEvent(late.EventType, late.TenantId, reason);

        if (reason == nameof(HighWaterSkipReason.Proven))
        {
            _logger.LogError(
                "Event {Sequence} ({EventType}) of stream {StreamId} for tenant {TenantId} on database {Database} committed inside the sequence range ({Starting}, {Ending}] after the high water mark skipped it, but that range was skipped because no transaction that could fill it was alive. This is a bug in Marten's high water detection, please report it. Async projections and subscriptions that were already past the event did not see it",
                late.Sequence, late.EventType, late.StreamId, late.TenantId, _database.Identifier, late.Starting,
                late.Ending);
        }
        else
        {
            _logger.LogWarning(
                "Event {Sequence} ({EventType}) of stream {StreamId} for tenant {TenantId} on database {Database} committed inside the sequence range ({Starting}, {Ending}] after the high water mark skipped it ({Reason}). Async projections and subscriptions that were already past the event did not see it. Async single stream projections rebuild the stream; other projections and subscriptions are not repaired",
                late.Sequence, late.EventType, late.StreamId, late.TenantId, _database.Identifier, late.Starting,
                late.Ending, reason);
        }
    }

    // Every async single-stream projection of this store owes each late event a repair, until it rebuilds the
    // event's stream or finds that it does not handle the event's type
    private string[] repairableProjectionNames()
    {
        return _graph.Options.Projections.All
            .Where(x => x is IAggregateProjection
            {
                Scope: AggregationScope.SingleStream, Lifecycle: ProjectionLifecycle.Async
            })
            .Select(x => x.Name)
            .Distinct()
            .ToArray();
    }

    private void startRepairs(IReadOnlyList<PendingRepair> pending, CancellationToken token)
    {
        lock (_repairLock)
        {
            // One pass at a time. A pass waits on shard batches, so it can outlive a probe interval.
            if (!_repairs.IsCompleted) return;

            _repairs = Task.Run(() => repairAsync(pending, token), CancellationToken.None);
        }
    }

    private async Task repairAsync(IReadOnlyList<PendingRepair> pending, CancellationToken token)
    {
        var store = _graph.Store;
        if (store == null) return;

        var done = new List<(long Sequence, string TenantId, string Projection)>();

        var groups = pending
            .SelectMany(p => p.Owed.Select(name => (Projection: name, Event: p)))
            .GroupBy(x => (x.Projection, x.Event.TenantId, x.Event.StreamId));

        foreach (var group in groups)
        {
            if (token.IsCancellationRequested) break;

            var (name, tenantId, streamId) = group.Key;
            var events = group.Select(x => x.Event).ToList();

            if (!_graph.Options.Projections.TryFindProjection(name, out var source)
                || source is not IAggregateProjection
                {
                    Scope: AggregationScope.SingleStream, Lifecycle: ProjectionLifecycle.Async
                } aggregate)
            {
                continue;
            }

            var relevant = events.Where(x => handles(aggregate, x.EventType)).ToList();
            done.AddRange(events.Except(relevant).Select(x => (x.Sequence, x.TenantId, name)));
            if (relevant.Count == 0) continue;

            // Only the node running the shard can rebuild inside it. Leave it for that node's probe otherwise.
            var daemon = daemonRunning(store, source, tenantId);
            if (daemon == null)
            {
                _logger.LogDebug(
                    "No daemon in this process runs projection {Projection} on database {Database}, so the late event(s) {Sequences} of stream {StreamId} are left for the node that does",
                    name, _database.Identifier, relevant.Select(x => x.Sequence).ToArray(), streamId);
                continue;
            }

            _logger.LogDebug("Rebuilding stream {StreamId} for tenant {TenantId} in projection {Projection} for the late event(s) {Sequences}",
                streamId, tenantId, name, relevant.Select(x => x.Sequence).ToArray());

            try
            {
                var identity = DocumentStore.DocumentIdentityFor(aggregate.IdentityType, streamIdFor(streamId));
                var result = await daemon.RebuildIdentityAsync(name, tenantId, identity, token).ConfigureAwait(false);

                // The re-fold covers the stream up to the batch it was bound to. An event above that is not in
                // the document yet, and the shard may not reach it either, so it stays owed.
                var repaired = relevant.Where(x => x.Sequence <= result.BoundCeiling).ToList();
                done.AddRange(repaired.Select(x => (x.Sequence, x.TenantId, name)));

                if (repaired.Count > 0)
                {
                    _logger.LogInformation(
                        "Rebuilt stream {StreamId} for tenant {TenantId} in projection {Projection} to take in the late event(s) {Sequences}",
                        streamId, tenantId, name, repaired.Select(x => x.Sequence).ToArray());
                }
            }
            catch (IdentityRebuildRefusedException e) when (e.Reason is IdentityRebuildRefusal.NotASingleStreamProjection
                                                                or IdentityRebuildRefusal.NotSupportedByStore)
            {
                _logger.LogWarning(e,
                    "Projection {Projection} cannot rebuild stream {StreamId} for tenant {TenantId} to take in the late event(s) {Sequences}. Rebuild the projection to include them",
                    name, streamId, tenantId, relevant.Select(x => x.Sequence).ToArray());
                done.AddRange(relevant.Select(x => (x.Sequence, x.TenantId, name)));
            }
            catch (IdentityRebuildRefusedException e)
            {
                // Stopped, paused, being rebuilt or never committed: try again on a later probe
                _logger.LogDebug(e, "Rebuilding stream {StreamId} of projection {Projection} for a late event was refused",
                    streamId, name);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                _logger.LogError(e,
                    "Unable to rebuild stream {StreamId} for tenant {TenantId} in projection {Projection} to take in the late event(s) {Sequences}. Will try again",
                    streamId, tenantId, name, relevant.Select(x => x.Sequence).ToArray());
            }
        }

        if (done.Count == 0) return;

        try
        {
            await markRepairedAsync(done, token).ConfigureAwait(false);
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            // The rebuilds are idempotent, so losing this only repeats them on the next probe
            _logger.LogError(e, "Unable to record the late event repairs on database {Database}", _database.Identifier);
        }
    }

    private async Task markRepairedAsync(IReadOnlyList<(long Sequence, string TenantId, string Projection)> done,
        CancellationToken token)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var batch = conn.CreateBatch();
        foreach (var (sequence, tenantId, projection) in done)
        {
            var command = new NpgsqlBatchCommand(
                $"update {_graph.DatabaseSchemaName}.{LateEventsTable.Name} set repaired_projections = array_append(coalesce(repaired_projections, '{{}}'), $1) where seq_id = $2 and tenant_id = $3 and not ($1 = any(coalesce(repaired_projections, '{{}}')))");
            command.Parameters.Add(new NpgsqlParameter { Value = projection, NpgsqlDbType = NpgsqlDbType.Varchar });
            command.Parameters.Add(new NpgsqlParameter { Value = sequence, NpgsqlDbType = NpgsqlDbType.Bigint });
            command.Parameters.Add(new NpgsqlParameter { Value = tenantId, NpgsqlDbType = NpgsqlDbType.Varchar });
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private ProjectionDaemon? daemonRunning(DocumentStore store, IProjectionSource<IDocumentOperations, IQuerySession> source,
        string tenantId)
    {
        var shards = source.Shards().Select(x => x.Name).ToList();
        if (_graph.UseTenantPartitionedEvents)
        {
            shards.AddRange(shards.Select(x => x.ForTenant(tenantId)).ToArray());
        }

        return store.DaemonsInThisProcess().FirstOrDefault(daemon =>
            string.Equals(daemon.Database.Identifier, _database.Identifier, StringComparison.OrdinalIgnoreCase)
            && shards.Any(x => daemon.StatusFor(x.Identity) == AgentStatus.Running));
    }

    // An event type this store cannot resolve is treated as handled: rebuilding a stream is never wrong, only wasted
    private bool handles(IAggregateProjection aggregate, string eventTypeName)
    {
        var mapping = _graph.EventMappingFor(eventTypeName);
        if (mapping == null || aggregate.AllEventTypes.Length == 0) return true;

        return aggregate.AllEventTypes.Any(x => x.IsAssignableFrom(mapping.DocumentType));
    }

    private object streamIdFor(string streamId)
    {
        return _graph.StreamIdentity == StreamIdentity.AsGuid ? Guid.Parse(streamId) : streamId;
    }

    private sealed record LateEvent(long Sequence, string TenantId, string StreamId, string EventType, long Starting,
        long Ending, string? Reason);

    private sealed record PendingRepair(long Sequence, string TenantId, string StreamId, string EventType,
        string[] Repaired)
    {
        public string[] Owed { get; set; } = [];
    }
}
