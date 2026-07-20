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
///     reports Healthy. This check instead compares the high-water mark against the actual
///     highest event sequence and reports Unhealthy only when the mark stops advancing while
///     events keep accumulating past it.
/// </summary>
public static class HighWaterHealthCheckExtensions
{
    /// <summary>
    ///     Adds a health check that reports <see cref="HealthCheckResult.Unhealthy" /> when the
    ///     high-water mark has stopped advancing for at least <paramref name="staleThreshold" />
    ///     while later events remain unprocessed — a strong signal that the high-water agent has
    ///     died or wedged (marten#4961).
    ///     <para>
    ///         This is a Phase 0 (marten#4982) implementation built on the sequence-gap heuristic
    ///         so it ships without an upstream dependency. A non-zero gap is normal transiently
    ///         (the detector holds the mark inside a "safe harbor" behind in-flight/gapped
    ///         sequences), so the check trips only on a <em>sustained non-advance</em>, never on a
    ///         single snapshot. The reading is store-global database state, so it is correct on any
    ///         node regardless of which one runs the agent — it fails only when <em>no</em> node is
    ///         advancing the mark.
    ///     </para>
    /// </summary>
    /// <param name="builder"><see cref="IHealthChecksBuilder" /></param>
    /// <param name="staleThreshold">
    ///     How long the high-water mark may sit unchanged while behind the latest event sequence
    ///     before the store is considered unhealthy. Defaults to 30 seconds.
    /// </param>
    /// <param name="minimumGap">
    ///     The gap (highest event sequence minus high-water mark) that is treated as "caught up"
    ///     and never trips the check, absorbing the normal safe-harbor lag. Defaults to 1.
    /// </param>
    public static IHealthChecksBuilder AddMartenHighWaterHealthCheck(
        this IHealthChecksBuilder builder,
        TimeSpan? staleThreshold = null,
        long minimumGap = 1
    )
    {
        builder.Services.AddSingleton(new HighWaterHealthCheckSettings(
            staleThreshold ?? TimeSpan.FromSeconds(30), minimumGap));
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
    public record HighWaterHealthCheckSettings(TimeSpan StaleThreshold, long MinimumGap);

    /// <summary>
    ///     Tracks, per database, when the high-water mark was first observed to be behind the
    ///     latest event sequence and what value it held then, so a <em>sustained</em> non-advance
    ///     can be distinguished from a transient safe-harbor gap across health check invocations.
    /// </summary>
    public class HighWaterStateTracker
    {
        public ConcurrentDictionary<string, (DateTimeOffset FirstObservedAt, long HighWaterMark)> Readings { get; } =
            new();
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
        private readonly HighWaterStateTracker _tracker;

        public HighWaterHealthCheck(IDocumentStore store, HighWaterHealthCheckSettings settings,
            TimeProvider timeProvider, HighWaterStateTracker tracker)
        {
            _store = store;
            _timeProvider = timeProvider;
            _staleThreshold = settings.StaleThreshold;
            _minimumGap = settings.MinimumGap;
            _tracker = tracker;
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
                _tracker.Readings.TryRemove(database.Identifier, out _);
                return HealthCheckResult.Healthy("Healthy");
            }

            var highest = await database.FetchHighestEventSequenceNumber(token).ConfigureAwait(false);
            var gap = highest - highWater.Sequence;
            var now = _timeProvider.GetUtcNow();

            // Caught up (within the normal safe-harbor gap). Clear any stalled-mark tracking.
            if (gap <= _minimumGap)
            {
                _tracker.Readings.TryRemove(database.Identifier, out _);
                return HealthCheckResult.Healthy("Healthy");
            }

            // A gap exists. That is normal transiently; it is only a problem when the mark is NOT
            // advancing while events keep piling up beyond it. Track the first time we saw a gap at
            // this mark value; if the mark moves, reset the clock; if it stays put past the
            // threshold, the high-water agent has almost certainly died or wedged.
            var reading = _tracker.Readings.GetOrAdd(database.Identifier, _ => (now, highWater.Sequence));

            if (reading.HighWaterMark != highWater.Sequence)
            {
                _tracker.Readings[database.Identifier] = (now, highWater.Sequence);
                return HealthCheckResult.Healthy("Healthy");
            }

            if (now - reading.FirstObservedAt >= _staleThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Unhealthy: the high-water mark for database '{database.Identifier}' has been stuck at {highWater.Sequence} with {gap} later event(s) unprocessed (highest sequence {highest}) for at least {_staleThreshold}. The high-water agent may have stopped (see marten#4961).");
            }

            return HealthCheckResult.Healthy("Healthy");
        }
    }
}
