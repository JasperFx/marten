using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Weasel.Postgresql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NpgsqlTypes;

namespace Marten.Events.Daemon;

/// <summary>
///     Health check that detects a stalled or dead high-water agent — the failure mode in
///     marten#4961 that <see cref="AsyncDaemonHealthCheckExtensions" /> cannot see. That check
///     measures projection lag <em>against</em> the high-water mark, so when the high-water
///     agent itself dies the mark freezes, projections catch up to the frozen value, and it
///     reports Healthy. This check instead detects that the high-water agent has stopped and,
///     optionally, restarts it.
///     <para>
///         Two staleness signals, each used where it is actually valid (marten#4986, revised in
///         marten#5174):
///         <list type="number">
///             <item>
///                 <b>Poll-cycle age — per-tenant rows.</b> Under
///                 <c>UseTenantPartitionedEvents</c> every vectorized per-tenant poll re-stamps the
///                 <c>HighWaterMark:&lt;tenant&gt;</c> row through <c>mt_mark_event_progression</c>,
///                 which always sets <c>last_updated</c> — even when the mark does not move. Its age
///                 is therefore a direct signal that the loop is <em>cycling</em>, independent of
///                 whether the mark <em>advances</em>, so a quiet tenant never trips it. No extra
///                 write, and no dependency on ExtendedProgression.
///             </item>
///             <item>
///                 <b>Sequence gap — the store-global row.</b> There <c>last_updated</c> only moves
///                 when the mark advances, so it says nothing about liveness and the original
///                 heuristic is the honest one: the mark sitting unchanged while later events pile
///                 up past it.
///             </item>
///         </list>
///         The ExtendedProgression <c>heartbeat</c> column is deliberately <em>not</em> consulted:
///         it is never written for high-water rows (<c>ExtendedProgressionWriter.OnNext</c> drops
///         <c>HighWaterMark</c> states outright), so reading it only made the check look like it had
///         a signal it did not have — marten#5174.
///     </para>
///     <para>
///         marten#4991: on a multi-database (sharded / <c>MultiTenantedWithShardedDatabases</c>)
///         store the check probes <em>every</em> database by default. Pass a
///         <c>databaseFilter</c> to scope it to the databases this node actually hosts the daemon
///         for — otherwise a probe fans a connection out across all N databases and (with
///         <c>autoRestart</c>) would try to restart agents this node does not own. Under
///         <c>UseTenantPartitionedEvents</c> the high-water mark is tracked per tenant
///         (<c>HighWaterMark:&lt;tenant&gt;</c> rows), and those are the rows evaluated.
///     </para>
/// </summary>
public static class HighWaterHealthCheckExtensions
{
    /// <summary>
    ///     Adds a health check that reports <see cref="HealthCheckResult.Unhealthy" /> when the
    ///     high-water agent has stopped for at least <paramref name="staleThreshold" /> — via the age
    ///     of its last completed poll cycle on per-tenant rows, otherwise via the sequence-gap
    ///     heuristic (marten#4961 / marten#4986 / marten#5174).
    /// </summary>
    /// <param name="builder"><see cref="IHealthChecksBuilder" /></param>
    /// <param name="staleThreshold">
    ///     How long the high-water agent may go without completing a poll cycle (or, on the
    ///     store-global gap path, how long the mark may sit unchanged while behind the latest event
    ///     sequence) before the store is considered unhealthy. Defaults to 30 seconds.
    /// </param>
    /// <param name="minimumGap">
    ///     Store-global gap path only: the gap (highest event sequence minus high-water mark) that is
    ///     treated as "caught up" and never trips the check, absorbing the normal safe-harbor lag.
    ///     Defaults to 1.
    /// </param>
    /// <param name="autoRestart">
    ///     When <c>true</c>, an Unhealthy result also asks the local projection coordinator to
    ///     restart the high-water agent's poll loop for the affected database
    ///     (<see cref="IProjectionDaemon.RestartHighWaterAgentAsync" />). The restart never advances
    ///     the mark and is capped to once per <paramref name="staleThreshold" /> window per database
    ///     to avoid churn; the cycle is still reported <b>Unhealthy</b> so an alert fires. Defaults
    ///     to <c>false</c> (detection only). Intended for single-writer (Solo) or leader nodes —
    ///     the process running the health check must be the one hosting the daemon.
    /// </param>
    /// <param name="databaseFilter">
    ///     marten#4991: optional predicate restricting the check to a subset of the store's
    ///     databases. On a sharded / multi-tenant store where daemon distribution is spread across
    ///     nodes (e.g. Wolverine-managed), scope this to the databases whose async agents run on
    ///     the local node so the probe does not fan out to (or auto-restart agents on) databases
    ///     this node does not host. Defaults to <c>null</c> (every database — today's behavior).
    ///     <para>
    ///         This predicate is captured at registration time, so it cannot resolve services. When
    ///         ownership is runtime state that has to be re-read on each probe — Wolverine-managed
    ///         daemon distribution being the usual case — use the overload taking a
    ///         <c>Func&lt;IServiceProvider, IMartenDatabase, bool&gt;</c> instead (marten#5061).
    ///     </para>
    /// </param>
    /// <param name="includeExternallyManaged">
    ///     marten#4991: by default the check only runs under <see cref="DaemonMode.Solo" /> /
    ///     <see cref="DaemonMode.HotCold" />, because in <see cref="DaemonMode.ExternallyManaged" />
    ///     this store hosts no daemon and a frozen mark is legitimate. Set this to <c>true</c> to
    ///     also assert under <see cref="DaemonMode.ExternallyManaged" /> (e.g. Wolverine-managed
    ///     distribution) — in which case only the per-tenant poll-cycle signal is used, never the
    ///     store-global gap heuristic, since an external owner can legitimately pause the mark.
    ///     A non-partitioned store therefore has nothing to assert on there. Combine with
    ///     <paramref name="databaseFilter" /> so the check only asserts on databases the local node
    ///     actually owns. Defaults to <c>false</c>.
    /// </param>
    public static IHealthChecksBuilder AddMartenHighWaterHealthCheck(
        this IHealthChecksBuilder builder,
        TimeSpan? staleThreshold = null,
        long minimumGap = 1,
        bool autoRestart = false,
        Func<IMartenDatabase, bool>? databaseFilter = null,
        bool includeExternallyManaged = false
    )
    {
        return builder.registerHighWaterHealthCheck(new HighWaterHealthCheckSettings(
            staleThreshold ?? TimeSpan.FromSeconds(30), minimumGap, autoRestart, databaseFilter,
            includeExternallyManaged));
    }

    /// <summary>
    ///     marten#5061: same check, but with a <paramref name="databaseFilter" /> that is handed the
    ///     <see cref="IServiceProvider" /> and re-evaluated on <em>every</em> probe. Use this
    ///     overload when "the databases this node owns" is runtime state rather than something known
    ///     at registration time — the common case under Wolverine-managed daemon distribution, where
    ///     agent assignments move between nodes over the process's lifetime:
    ///     <code>
    ///     Services.AddHealthChecks().AddMartenHighWaterHealthCheck(
    ///         (services, database) => services.GetRequiredService&lt;IWolverineRuntime&gt;()
    ///             .Agents.AllLocallyOwnedDatabaseIds()
    ///             .Any(id =&gt; id.Name.EqualsIgnoreCase(database.Identifier)),
    ///         staleThreshold: TimeSpan.FromSeconds(30),
    ///         includeExternallyManaged: true);
    ///     </code>
    ///     The <see cref="IServiceProvider" /> passed in is the one the health check itself was
    ///     resolved from, so scoped services are legal.
    /// </summary>
    /// <param name="builder"><see cref="IHealthChecksBuilder" /></param>
    /// <param name="databaseFilter">
    ///     Provider-aware predicate, invoked once per database per probe. Return <c>true</c> for the
    ///     databases this node should assert on.
    /// </param>
    /// <param name="staleThreshold">See the other overload. Defaults to 30 seconds.</param>
    /// <param name="minimumGap">See the other overload. Defaults to 1.</param>
    /// <param name="autoRestart">See the other overload. Defaults to <c>false</c>.</param>
    /// <param name="includeExternallyManaged">
    ///     See the other overload. Usually <c>true</c> when this overload is what you need, since
    ///     runtime-assigned ownership implies <see cref="DaemonMode.ExternallyManaged" />. Defaults
    ///     to <c>false</c>.
    /// </param>
    public static IHealthChecksBuilder AddMartenHighWaterHealthCheck(
        this IHealthChecksBuilder builder,
        Func<IServiceProvider, IMartenDatabase, bool> databaseFilter,
        TimeSpan? staleThreshold = null,
        long minimumGap = 1,
        bool autoRestart = false,
        bool includeExternallyManaged = false
    )
    {
        ArgumentNullException.ThrowIfNull(databaseFilter);

        return builder.registerHighWaterHealthCheck(new HighWaterHealthCheckSettings(
            staleThreshold ?? TimeSpan.FromSeconds(30), minimumGap, autoRestart, DatabaseFilter: null,
            includeExternallyManaged, databaseFilter));
    }

    private static IHealthChecksBuilder registerHighWaterHealthCheck(this IHealthChecksBuilder builder,
        HighWaterHealthCheckSettings settings)
    {
        // marten#5061: registered as a factory rather than a bare instance so an application that
        // needs to reach further into DI than ScopedDatabaseFilter allows still has a supported
        // override point (Replace a Singleton factory, not a Singleton instance).
        builder.Services.Replace(ServiceDescriptor.Singleton(_ => settings));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<HighWaterStateTracker>();
        return builder.AddCheck<HighWaterHealthCheck>(
            nameof(HighWaterHealthCheck),
            tags: new[] { "Marten", "AsyncDaemon", "HighWater" }
        );
    }

    /// <summary>
    ///     DI-injected settings for <see cref="HighWaterHealthCheck" />.
    /// </summary>
    /// <param name="ScopedDatabaseFilter">
    ///     marten#5061: provider-aware alternative to <paramref name="DatabaseFilter" />, resolved
    ///     against the health check's own <see cref="IServiceProvider" /> on every probe. When both
    ///     are set a database must satisfy both.
    /// </param>
    public record HighWaterHealthCheckSettings(
        TimeSpan StaleThreshold,
        long MinimumGap,
        bool AutoRestart = false,
        Func<IMartenDatabase, bool>? DatabaseFilter = null,
        bool IncludeExternallyManaged = false,
        Func<IServiceProvider, IMartenDatabase, bool>? ScopedDatabaseFilter = null);

    /// <summary>
    ///     Tracks the fallback gap heuristic's "first observed a stuck mark" reading (keyed per
    ///     database + high-water shard, so per-tenant marks are tracked independently) and, when
    ///     <c>autoRestart</c> is on, the last auto-restart moment per database, so a
    ///     <em>sustained</em> non-advance can be distinguished from a transient safe-harbor gap and
    ///     restarts can be capped to once per staleness window across health check invocations.
    /// </summary>
    public class HighWaterStateTracker
    {
        public ConcurrentDictionary<string, (DateTimeOffset FirstObservedAt, long HighWaterMark)> Readings { get; } =
            new();

        public ConcurrentDictionary<string, DateTimeOffset> Restarts { get; } = new();
    }

    /// <summary>
    ///     Health check implementation.
    /// </summary>
    public class HighWaterHealthCheck: IHealthCheck
    {
        // The store-global high-water identity and the per-tenant prefix — the canonical grammar the
        // high-water machinery writes and reads (Marten.Events.Daemon.HighWater.HighWaterShardIdentity).
        private const string HighWaterMarkShard = HighWaterShardIdentity.StoreGlobal;
        private const string PerTenantHighWaterPrefix = HighWaterShardIdentity.PerTenantPrefix;

        private readonly IDocumentStore _store;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _staleThreshold;
        private readonly long _minimumGap;
        private readonly bool _autoRestart;
        private readonly Func<IMartenDatabase, bool>? _databaseFilter;
        private readonly Func<IServiceProvider, IMartenDatabase, bool>? _scopedDatabaseFilter;
        private readonly bool _includeExternallyManaged;
        private readonly HighWaterStateTracker _tracker;
        private readonly IServiceProvider _services;

        public HighWaterHealthCheck(IDocumentStore store, HighWaterHealthCheckSettings settings,
            TimeProvider timeProvider, HighWaterStateTracker tracker, IServiceProvider services)
        {
            _store = store;
            _timeProvider = timeProvider;
            _staleThreshold = settings.StaleThreshold;
            _minimumGap = settings.MinimumGap;
            _autoRestart = settings.AutoRestart;
            _databaseFilter = settings.DatabaseFilter;
            _scopedDatabaseFilter = settings.ScopedDatabaseFilter;
            _includeExternallyManaged = settings.IncludeExternallyManaged;
            _tracker = tracker;
            _services = services;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                // Gate: the high-water mark is only expected to advance when this store is actually
                // responsible for running the async daemon. Otherwise a frozen mark is legitimate
                // and asserting on it would be a false positive.
                if (_store.Options is not StoreOptions options)
                {
                    return HealthCheckResult.Healthy("Healthy");
                }

                var projections = options.Projections;

                // No async projections or subscriptions -> no high-water agent runs anywhere.
                if (!projections.HasAnyAsyncProjections())
                {
                    return HealthCheckResult.Healthy("No async projections or subscriptions are registered");
                }

                // Solo / HotCold host the daemon here, so use every available signal. ExternallyManaged
                // (e.g. Wolverine-managed distribution, marten#4991) hosts no local daemon — opt in with
                // includeExternallyManaged to still assert, but only via the per-tenant poll-cycle signal
                // (the store-global gap heuristic would false-positive when an external owner legitimately
                // pauses the mark). Disabled and everything else stays a no-op.
                var mode = projections.AsyncMode;
                bool skipGapHeuristic;
                if (mode is DaemonMode.Solo or DaemonMode.HotCold)
                {
                    skipGapHeuristic = false;
                }
                else if (mode == DaemonMode.ExternallyManaged && _includeExternallyManaged)
                {
                    skipGapHeuristic = true;
                }
                else
                {
                    return HealthCheckResult.Healthy(
                        $"Async daemon mode is {mode}; high-water is not advanced by this store");
                }

                // marten#4991: under UseTenantPartitionedEvents high water is tracked per tenant
                // (HighWaterMark:<tenant> rows) and the store-global HighWaterMark row is intentionally
                // frozen (the daemon skips the store-global loop) — so which rows are authoritative
                // depends on the mode. Evaluate per-tenant rows there, the store-global row otherwise.
                var perTenantHighWater = options.Events.UseTenantPartitionedEvents;

                var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);

                // marten#4991: scope to the databases this node owns so the probe does not fan out
                // to (or auto-restart) databases the local node does not host the daemon for.
                // marten#5061: ScopedDatabaseFilter is evaluated here, per probe, against the
                // provider the check was resolved from — that is the only way to express ownership
                // that is runtime state (Wolverine reassigns agents over a node's lifetime) rather
                // than something fixed at registration. When both filters are set, both must pass.
                IEnumerable<IMartenDatabase> scoped = databases;
                if (_databaseFilter != null)
                {
                    scoped = scoped.Where(_databaseFilter);
                }

                if (_scopedDatabaseFilter != null)
                {
                    scoped = scoped.Where(database => _scopedDatabaseFilter(_services, database));
                }

                foreach (var database in scoped)
                {
                    var result = await checkDatabaseAsync(database, skipGapHeuristic, perTenantHighWater,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (result.Status != HealthStatus.Healthy)
                    {
                        return result;
                    }
                }

                return HealthCheckResult.Healthy("Healthy");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy($"Unhealthy: {ex.Message}", ex);
            }
        }

        private async Task<HealthCheckResult> checkDatabaseAsync(IMartenDatabase database, bool skipGapHeuristic,
            bool perTenantHighWater, CancellationToken token)
        {
            var allHighWater = await readHighWaterRowsAsync(database, _store.Options.Events.DatabaseSchemaName, token)
                .ConfigureAwait(false);

            // marten#4991: under UseTenantPartitionedEvents the authoritative rows are the per-tenant
            // HighWaterMark:<tenant> rows (the original check matched only the exact "HighWaterMark"
            // string and was blind to a stalled per-tenant agent); otherwise it is the store-global
            // HighWaterMark row. Detect per-tenant mode from the configured flag OR from the presence of
            // per-tenant rows, and evaluate only the authoritative set — so the store-global row (which is
            // intentionally frozen under partitioning, the daemon skips its loop) is never gap-assessed
            // there and can't false-positive.
            var perTenantMode = perTenantHighWater || allHighWater.Any(x => x.IsPerTenant);

            var highWaterRows = allHighWater.Where(x => x.IsPerTenant == perTenantMode).ToArray();

            // No HighWaterMark progression row yet -> the daemon has not started here. Nothing to assert.
            if (highWaterRows.Length == 0)
            {
                clearTrackingForDatabase(database.Identifier);
                return HealthCheckResult.Healthy("Healthy");
            }

            var now = _timeProvider.GetUtcNow();
            long? highest = null; // fetched lazily, only for a store-global gap fallback

            foreach (var row in highWaterRows)
            {
                var key = trackingKey(database.Identifier, row.ShardName);

                // Primary signal, per-tenant rows only (marten#5174). Every vectorized per-tenant poll
                // re-stamps HighWaterMark:<tenant> through mt_mark_event_progression, which always sets
                // last_updated = transaction_timestamp() — even when the mark does not move. So its age
                // proves the poll loop is *cycling* independent of whether the mark *advances*, which is
                // what a liveness check needs and what no other persisted column offers here. It costs
                // nothing extra: the write already happens on every cycle.
                //
                // This replaces the ExtendedProgression `heartbeat` column, which this check used to read
                // as its primary signal. That column is never written for high-water rows —
                // ExtendedProgressionWriter.OnNext drops HighWaterMark states outright (pinned by
                // skips_high_water_mark_and_all_projections_states) — so the branch was unreachable in any
                // real deployment and the check silently degraded to the gap heuristic.
                if (row.IsPerTenant)
                {
                    // The gap tracker is only for the store-global fallback path; keep it clear here.
                    _tracker.Readings.TryRemove(key, out _);

                    if (row.LastUpdated is not { } lastUpdated)
                    {
                        continue;
                    }

                    var age = now - lastUpdated;
                    if (age < _staleThreshold)
                    {
                        continue;
                    }

                    var restartNote = await tryAutoRestartAsync(database.Identifier, now, token).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy(
                        $"Unhealthy: the high-water agent for '{shardDescription(database, row)}' last completed a poll cycle {age.TotalSeconds:F0}s ago (at {lastUpdated:O}), exceeding the {_staleThreshold} staleness threshold. Its poll loop has stopped cycling (see marten#4961).{restartNote}");
                }

                // Store-global row. last_updated only moves when the mark ADVANCES here
                // (HighWaterDetector.persistDetectedMarkAsync returns early when nothing changed), so it is
                // not a liveness signal and the gap heuristic is the only honest one. It is unusable under
                // skipGapHeuristic (ExternallyManaged), where an external owner may legitimately pause the mark.
                if (skipGapHeuristic)
                {
                    _tracker.Readings.TryRemove(key, out _);
                    continue;
                }

                highest ??= await database.FetchHighestEventSequenceNumber(token).ConfigureAwait(false);
                var gap = highest.Value - row.Sequence;

                // Caught up (within the normal safe-harbor gap). Clear any stalled-mark tracking.
                if (gap <= _minimumGap)
                {
                    _tracker.Readings.TryRemove(key, out _);
                    continue;
                }

                // Track the first time we saw a gap at this mark value; if the mark moves, reset the clock;
                // if it stays put past the threshold, the high-water agent has almost certainly died or wedged.
                var reading = _tracker.Readings.GetOrAdd(key, _ => (now, row.Sequence));

                if (reading.HighWaterMark != row.Sequence)
                {
                    _tracker.Readings[key] = (now, row.Sequence);
                    continue;
                }

                if (now - reading.FirstObservedAt >= _staleThreshold)
                {
                    var restartNote = await tryAutoRestartAsync(database.Identifier, now, token).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy(
                        $"Unhealthy: the high-water mark for '{shardDescription(database, row)}' has been stuck at {row.Sequence} with {gap} later event(s) unprocessed (highest sequence {highest.Value}) for at least {_staleThreshold}. The high-water agent may have stopped (see marten#4961).{restartNote}");
                }
            }

            // Every high-water row for this database is healthy -> clear the restart cap so a future
            // stall can be remediated immediately.
            _tracker.Restarts.TryRemove(database.Identifier, out _);
            return HealthCheckResult.Healthy("Healthy");
        }

        /// <summary>
        ///     marten#5174: read just the high-water progression rows, with <c>last_updated</c>. This used
        ///     to go through <see cref="IEventDatabase.AllProjectionProgress(CancellationToken)" /> and
        ///     filter in memory, which pulled every projection × tenant row on every probe and still could
        ///     not see <c>last_updated</c> — <see cref="JasperFx.Events.Projections.ShardState" /> has no
        ///     field for it. Two high-water rows out of hundreds is the whole working set here.
        /// </summary>
        private static async Task<IReadOnlyList<HighWaterRow>> readHighWaterRowsAsync(IMartenDatabase database,
            string schema, CancellationToken token)
        {
            await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

            var rows = new List<HighWaterRow>();

            await using var conn = database.CreateConnection();
            await conn.OpenAsync(token).ConfigureAwait(false);
            try
            {
                // Both operands are compile-time constants from HighWaterShardIdentity, so there is no
                // pattern grammar reaching this from user input.
                await using var reader = await conn
                    .CreateCommand(
                        $"select name, last_seq_id, last_updated from {schema}.mt_event_progression where name = :global or name like :prefix")
                    .With("global", HighWaterMarkShard, NpgsqlDbType.Varchar)
                    .With("prefix", PerTenantHighWaterPrefix + "%", NpgsqlDbType.Varchar)
                    .ExecuteReaderAsync(token).ConfigureAwait(false);

                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var name = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                    var sequence = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
                    DateTimeOffset? lastUpdated = await reader.IsDBNullAsync(2, token).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<DateTimeOffset>(2, token).ConfigureAwait(false);

                    rows.Add(new HighWaterRow(name, sequence, lastUpdated));
                }
            }
            finally
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }

            return rows;
        }

        private record HighWaterRow(string ShardName, long Sequence, DateTimeOffset? LastUpdated)
        {
            public bool IsPerTenant { get; } =
                !string.Equals(ShardName, HighWaterMarkShard, StringComparison.Ordinal);
        }

        private static string shardDescription(IMartenDatabase database, HighWaterRow row)
        {
            return row.IsPerTenant
                ? $"database '{database.Identifier}', shard '{row.ShardName}'"
                : $"database '{database.Identifier}'";
        }

        private static string trackingKey(string databaseIdentifier, string shardName) =>
            databaseIdentifier + "|" + shardName;

        private void clearTrackingForDatabase(string databaseIdentifier)
        {
            var prefix = databaseIdentifier + "|";
            foreach (var readingKey in _tracker.Readings.Keys)
            {
                if (readingKey.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _tracker.Readings.TryRemove(readingKey, out _);
                }
            }

            _tracker.Restarts.TryRemove(databaseIdentifier, out _);
        }

        // marten#4986 item 1: opt-in remediation. Ask the local coordinator's daemon to restart the
        // high-water poll loop for this database — loop only, never advancing the mark. Best-effort and
        // capped to once per staleness window so the (faster) health-check cadence can't thrash a loop
        // that legitimately needs longer to re-establish. The cycle is still reported Unhealthy by the
        // caller so an alert fires regardless.
        private async Task<string> tryAutoRestartAsync(string databaseIdentifier, DateTimeOffset now,
            CancellationToken token)
        {
            if (!_autoRestart)
            {
                return string.Empty;
            }

            if (_tracker.Restarts.TryGetValue(databaseIdentifier, out var lastRestart) &&
                now - lastRestart < _staleThreshold)
            {
                return " An auto-restart was already attempted within the current staleness window.";
            }

            var coordinator = _services.GetService<IProjectionCoordinator>();
            if (coordinator is null)
            {
                return " (autoRestart is enabled but no IProjectionCoordinator is registered to restart the agent.)";
            }

            try
            {
                var daemon = await coordinator.DaemonForDatabase(databaseIdentifier).ConfigureAwait(false);
                await daemon.RestartHighWaterAgentAsync(token).ConfigureAwait(false);
                _tracker.Restarts[databaseIdentifier] = now;
                return " An auto-restart of the high-water agent was triggered (the mark was NOT advanced).";
            }
            catch (Exception e)
            {
                return $" An auto-restart of the high-water agent was attempted but failed: {e.Message}.";
            }
        }
    }
}
