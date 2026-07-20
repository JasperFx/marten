using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
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
///                 persisted, so the check falls back to the original heuristic: the mark sitting
///                 unchanged while later events pile up past it.
///             </item>
///         </list>
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
    public static IHealthChecksBuilder AddMartenHighWaterHealthCheck(
        this IHealthChecksBuilder builder,
        TimeSpan? staleThreshold = null,
        long minimumGap = 1,
        bool autoRestart = false
    )
    {
        builder.Services.AddSingleton(new HighWaterHealthCheckSettings(
            staleThreshold ?? TimeSpan.FromSeconds(30), minimumGap, autoRestart));
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
    public record HighWaterHealthCheckSettings(TimeSpan StaleThreshold, long MinimumGap, bool AutoRestart = false);

    /// <summary>
    ///     Tracks, per database, the fallback gap heuristic's "first observed a stuck mark" reading
    ///     and (when <c>autoRestart</c> is on) the last auto-restart moment, so a <em>sustained</em>
    ///     non-advance can be distinguished from a transient safe-harbor gap and restarts can be
    ///     capped to once per staleness window across health check invocations.
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
        private const string HighWaterMarkShard = "HighWaterMark";

        private readonly IDocumentStore _store;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _staleThreshold;
        private readonly long _minimumGap;
        private readonly bool _autoRestart;
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

                // Disabled / ExternallyManaged -> this store hosts no daemon, so it must not assert
                // that some local agent is advancing the mark.
                if (projections.AsyncMode is not (DaemonMode.Solo or DaemonMode.HotCold))
                {
                    return HealthCheckResult.Healthy(
                        $"Async daemon mode is {projections.AsyncMode}; high-water is not advanced by this store");
                }

                var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);

                foreach (var database in databases)
                {
                    var result = await checkDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
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

        private async Task<HealthCheckResult> checkDatabaseAsync(IMartenDatabase database, CancellationToken token)
        {
            var allProgress = await database.AllProjectionProgress(token).ConfigureAwait(false);
            var highWater = allProgress.FirstOrDefault(x => string.Equals(HighWaterMarkShard, x.ShardName));

            // No HighWaterMark progression row yet -> the daemon has not started here. Nothing to assert.
            if (highWater is null)
            {
                clearTracking(database.Identifier);
                return HealthCheckResult.Healthy("Healthy");
            }

            var now = _timeProvider.GetUtcNow();

            // Phase 2 (marten#4986) primary signal: the liveness heartbeat (jasperfx#539). Present only
            // when ExtendedProgression is enabled. Heartbeat age proves the poll loop is *cycling*
            // independent of whether the mark *advances*, so a quiet store never trips it — a strictly
            // better signal than the gap heuristic. Use it whenever it is available.
            if (highWater.LastHeartbeat is { } lastHeartbeat)
            {
                // The gap tracker is only for the fallback path; keep it clear while on the heartbeat path.
                _tracker.Readings.TryRemove(database.Identifier, out _);

                var age = now - lastHeartbeat;
                if (age < _staleThreshold)
                {
                    _tracker.Restarts.TryRemove(database.Identifier, out _);
                    return HealthCheckResult.Healthy("Healthy");
                }

                var restartNote = await tryAutoRestartAsync(database.Identifier, now, token).ConfigureAwait(false);
                return HealthCheckResult.Unhealthy(
                    $"Unhealthy: the high-water agent for database '{database.Identifier}' last reported a liveness heartbeat {age.TotalSeconds:F0}s ago (at {lastHeartbeat:O}), exceeding the {_staleThreshold} staleness threshold. Its poll loop has stopped cycling (see JasperFx/jasperfx#539 / marten#4961).{restartNote}");
            }

            // Fallback (ExtendedProgression off): the sequence-gap heuristic. A non-zero gap is normal
            // transiently (the detector holds the mark inside a "safe harbor" behind in-flight/gapped
            // sequences), so this trips only on a sustained non-advance while events pile up past the mark.
            var highest = await database.FetchHighestEventSequenceNumber(token).ConfigureAwait(false);
            var gap = highest - highWater.Sequence;

            // Caught up (within the normal safe-harbor gap). Clear any stalled-mark tracking.
            if (gap <= _minimumGap)
            {
                clearTracking(database.Identifier);
                return HealthCheckResult.Healthy("Healthy");
            }

            // Track the first time we saw a gap at this mark value; if the mark moves, reset the clock; if
            // it stays put past the threshold, the high-water agent has almost certainly died or wedged.
            var reading = _tracker.Readings.GetOrAdd(database.Identifier, _ => (now, highWater.Sequence));

            if (reading.HighWaterMark != highWater.Sequence)
            {
                _tracker.Readings[database.Identifier] = (now, highWater.Sequence);
                _tracker.Restarts.TryRemove(database.Identifier, out _);
                return HealthCheckResult.Healthy("Healthy");
            }

            if (now - reading.FirstObservedAt >= _staleThreshold)
            {
                var restartNote = await tryAutoRestartAsync(database.Identifier, now, token).ConfigureAwait(false);
                return HealthCheckResult.Unhealthy(
                    $"Unhealthy: the high-water mark for database '{database.Identifier}' has been stuck at {highWater.Sequence} with {gap} later event(s) unprocessed (highest sequence {highest}) for at least {_staleThreshold}. The high-water agent may have stopped (see marten#4961).{restartNote}");
            }

            return HealthCheckResult.Healthy("Healthy");
        }

        private void clearTracking(string databaseIdentifier)
        {
            _tracker.Readings.TryRemove(databaseIdentifier, out _);
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
