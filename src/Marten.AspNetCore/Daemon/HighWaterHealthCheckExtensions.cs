using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marten.Events.Daemon;

/// <summary>
///     Health check that detects a stalled or dead high-water agent — the failure mode in
///     marten#4961 that <see cref="AsyncDaemonHealthCheckExtensions" /> cannot see. That check
///     measures projection lag <em>against</em> the high-water mark, so when the high-water
///     agent itself dies the mark freezes, projections catch up to the frozen value, and it
///     reports Healthy. This check instead detects that the high-water agent has stopped and,
///     optionally, restarts it.
///     <para>
///         Two staleness signals, best first (marten#4986):
///         <list type="number">
///             <item>
///                 <b>Heartbeat age (primary).</b> When ExtendedProgression is enabled, the
///                 high-water agent stamps a liveness heartbeat on the <c>HighWaterMark</c>
///                 progression row on every completed poll cycle (JasperFx/jasperfx#539). Its age
///                 is a direct signal that the loop is <em>cycling</em>, independent of whether the
///                 mark <em>advances</em> — so a quiet store with no new events never trips it.
///             </item>
///             <item>
///                 <b>Sequence gap (fallback).</b> When ExtendedProgression is off, no heartbeat is
///                 persisted, so the check falls back to the original heuristic: the store-global
///                 mark sitting unchanged while later events pile up past it.
///             </item>
///         </list>
///     </para>
///     <para>
///         marten#4991: on a multi-database (sharded / <c>MultiTenantedWithShardedDatabases</c>)
///         store the check probes <em>every</em> database by default. Pass a
///         <c>databaseFilter</c> to scope it to the databases this node actually hosts the daemon
///         for — otherwise a probe fans a connection out across all N databases and (with
///         <c>autoRestart</c>) would try to restart agents this node does not own. Under
///         <c>UseTenantPartitionedEvents</c> the high-water mark is tracked per tenant
///         (<c>HighWaterMark:&lt;tenant&gt;</c> rows), and those are evaluated too — via the
///         heartbeat signal, which is the only reliable per-tenant staleness signal.
///     </para>
/// </summary>
public static class HighWaterHealthCheckExtensions
{
    /// <summary>
    ///     Adds a health check that reports <see cref="HealthCheckResult.Unhealthy" /> when the
    ///     high-water agent has stopped for at least <paramref name="staleThreshold" /> — via its
    ///     liveness heartbeat where available, otherwise via the sequence-gap heuristic
    ///     (marten#4961 / marten#4986).
    /// </summary>
    /// <param name="builder"><see cref="IHealthChecksBuilder" /></param>
    /// <param name="staleThreshold">
    ///     How long the high-water agent may go without a heartbeat (or, on the fallback path, how
    ///     long the mark may sit unchanged while behind the latest event sequence) before the store
    ///     is considered unhealthy. Defaults to 30 seconds.
    /// </param>
    /// <param name="minimumGap">
    ///     Fallback path only: the gap (highest event sequence minus high-water mark) that is
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
    /// </param>
    /// <param name="includeExternallyManaged">
    ///     marten#4991: by default the check only runs under <see cref="DaemonMode.Solo" /> /
    ///     <see cref="DaemonMode.HotCold" />, because in <see cref="DaemonMode.ExternallyManaged" />
    ///     this store hosts no daemon and a frozen mark is legitimate. Set this to <c>true</c> to
    ///     also assert under <see cref="DaemonMode.ExternallyManaged" /> (e.g. Wolverine-managed
    ///     distribution) — in which case only the <em>heartbeat</em> signal is used, never the gap
    ///     fallback, since an external owner can legitimately pause the mark. Combine with
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
        builder.Services.AddSingleton(new HighWaterHealthCheckSettings(
            staleThreshold ?? TimeSpan.FromSeconds(30), minimumGap, autoRestart, databaseFilter,
            includeExternallyManaged));
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
    public record HighWaterHealthCheckSettings(
        TimeSpan StaleThreshold,
        long MinimumGap,
        bool AutoRestart = false,
        Func<IMartenDatabase, bool>? DatabaseFilter = null,
        bool IncludeExternallyManaged = false);

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
                // includeExternallyManaged to still assert, but only via the heartbeat signal (the gap
                // fallback would false-positive when an external owner legitimately pauses the mark).
                // Disabled and everything else stays a no-op.
                var mode = projections.AsyncMode;
                bool heartbeatOnly;
                if (mode is DaemonMode.Solo or DaemonMode.HotCold)
                {
                    heartbeatOnly = false;
                }
                else if (mode == DaemonMode.ExternallyManaged && _includeExternallyManaged)
                {
                    heartbeatOnly = true;
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
                IEnumerable<IMartenDatabase> scoped = databases;
                if (_databaseFilter != null)
                {
                    scoped = databases.Where(_databaseFilter);
                }

                foreach (var database in scoped)
                {
                    var result = await checkDatabaseAsync(database, heartbeatOnly, perTenantHighWater,
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

        private async Task<HealthCheckResult> checkDatabaseAsync(IMartenDatabase database, bool heartbeatOnly,
            bool perTenantHighWater, CancellationToken token)
        {
            var allProgress = await database.AllProjectionProgress(token).ConfigureAwait(false);

            var allHighWater = allProgress
                .Where(x => string.Equals(x.ShardName, HighWaterMarkShard, StringComparison.Ordinal)
                            || x.ShardName.StartsWith(PerTenantHighWaterPrefix, StringComparison.Ordinal))
                .ToArray();

            // marten#4991: under UseTenantPartitionedEvents the authoritative rows are the per-tenant
            // HighWaterMark:<tenant> rows (the original check matched only the exact "HighWaterMark"
            // string and was blind to a stalled per-tenant agent); otherwise it is the store-global
            // HighWaterMark row. Detect per-tenant mode from the configured flag OR from the presence of
            // per-tenant rows, and evaluate only the authoritative set — so the store-global row (which is
            // intentionally frozen under partitioning, the daemon skips its loop) is never gap-assessed
            // there and can't false-positive.
            var perTenantMode = perTenantHighWater ||
                                allHighWater.Any(x =>
                                    x.ShardName.StartsWith(PerTenantHighWaterPrefix, StringComparison.Ordinal));

            var highWaterRows = perTenantMode
                ? allHighWater
                    .Where(x => x.ShardName.StartsWith(PerTenantHighWaterPrefix, StringComparison.Ordinal))
                    .ToArray()
                : allHighWater
                    .Where(x => string.Equals(x.ShardName, HighWaterMarkShard, StringComparison.Ordinal))
                    .ToArray();

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
                var isPerTenant =
                    !string.Equals(row.ShardName, HighWaterMarkShard, StringComparison.Ordinal);
                var key = trackingKey(database.Identifier, row.ShardName);

                // Primary signal (marten#4986): the liveness heartbeat (jasperfx#539), present only when
                // ExtendedProgression is enabled. Heartbeat age proves the poll loop is *cycling*
                // independent of whether the mark *advances*, so a quiet store never trips it — a strictly
                // better signal than the gap heuristic, and the only reliable per-tenant signal. Use it
                // whenever it is available.
                if (row.LastHeartbeat is { } lastHeartbeat)
                {
                    // The gap tracker is only for the fallback path; keep it clear while on the heartbeat path.
                    _tracker.Readings.TryRemove(key, out _);

                    var age = now - lastHeartbeat;
                    if (age < _staleThreshold)
                    {
                        continue;
                    }

                    var restartNote = await tryAutoRestartAsync(database.Identifier, now, token).ConfigureAwait(false);
                    return HealthCheckResult.Unhealthy(
                        $"Unhealthy: the high-water agent for '{shardDescription(database, row)}' last reported a liveness heartbeat {age.TotalSeconds:F0}s ago (at {lastHeartbeat:O}), exceeding the {_staleThreshold} staleness threshold. Its poll loop has stopped cycling (see JasperFx/jasperfx#539 / marten#4961).{restartNote}");
                }

                // No heartbeat. The gap fallback is only reliable for the store-global mark under a mode
                // this store actually hosts:
                //  - heartbeatOnly (ExternallyManaged) -> an external owner may legitimately pause the mark.
                //  - per-tenant -> there is no per-tenant highest-sequence to compute a meaningful gap
                //    (FetchHighestEventSequenceNumber is store-global), so a tenant with no new events would
                //    look permanently "behind". Enable ExtendedProgression for per-tenant staleness.
                if (heartbeatOnly || isPerTenant)
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

        private static string shardDescription(IMartenDatabase database, JasperFx.Events.Projections.ShardState row)
        {
            return string.Equals(row.ShardName, HighWaterMarkShard, StringComparison.Ordinal)
                ? $"database '{database.Identifier}'"
                : $"database '{database.Identifier}', shard '{row.ShardName}'";
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
