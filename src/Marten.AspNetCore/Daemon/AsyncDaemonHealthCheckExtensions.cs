using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marten.Events.Daemon;

public static class AsyncDaemonHealthCheckExtensions
{
    /// <summary>
    ///     Adds a health check for Martens Async Daemon.
    ///     The health check will verify that no async projection progression is lagging behind more than the
    ///     <paramref name="maxEventLag" />
    ///     The check will return <see cref="HealthCheckResult.Unhealthy" /> if any progression is more than
    ///     <paramref name="maxEventLag" /> behind the highWaterMark OR if any exception is thrown while doing the check.
    ///     <example>
    ///         <code>
    /// Customized Injection Example: services.AddHealthChecks().AddAsyncDaemonHealthCheck(150);
    /// </code>
    ///     </example>
    ///     Also - remember to add <c>app.MapHealthChecks("/your-health-path")</c> to the middleware pipeline
    /// </summary>
    /// <param name="builder">
    ///     <see cref="IHealthChecksBuilder" />
    /// </param>
    /// <param name="maxEventLag">
    ///     (optional) Acceptable lag of an eventprojection before it's considered unhealthy - defaults
    ///     to 100
    /// </param>
    /// <param name="maxSameLagTime">
    ///     (optional) Treat projection as healthy if maxEventLag exceeded, but projection sequence
    ///     changed since last check in given time - defaults to null (uses just maxEventLag)
    /// </param>
    /// <returns>
    ///     If healthy: <see cref="HealthCheckResult.Healthy" /> - else <see cref="HealthCheckResult.Unhealthy" />
    /// </returns>
    public static IHealthChecksBuilder AddMartenAsyncDaemonHealthCheck(
        this IHealthChecksBuilder builder,
        int maxEventLag = 100,
        TimeSpan? maxSameLagTime = null
    )
    {
        builder.Services.AddSingleton(new AsyncDaemonHealthCheckSettings(maxEventLag, maxSameLagTime));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<ProjectionStateTracker>();
        return builder.AddCheck<AsyncDaemonHealthCheck>(
            nameof(AsyncDaemonHealthCheck),
            tags: new[] { "Marten", "AsyncDaemon" }
        );
    }

    /// <summary>
    ///     marten#6189: the async daemon health check for an ancillary store registered with
    ///     <c>AddMartenStore&lt;TStore&gt;()</c>. See the non-generic overload for what it checks.
    ///     <para>
    ///         Call it once per store. Each store's check is named <c>AsyncDaemonHealthCheck:&lt;store type&gt;</c>
    ///         and carries its own settings and lag tracking, so it never changes the default store's check.
    ///     </para>
    /// </summary>
    /// <typeparam name="TStore">The store's marker interface, as passed to <c>AddMartenStore&lt;TStore&gt;()</c>.</typeparam>
    /// <param name="builder"><see cref="IHealthChecksBuilder" /></param>
    /// <param name="maxEventLag">See the non-generic overload. Defaults to 100.</param>
    /// <param name="maxSameLagTime">See the non-generic overload. Defaults to null.</param>
    public static IHealthChecksBuilder AddMartenAsyncDaemonHealthCheck<TStore>(
        this IHealthChecksBuilder builder,
        int maxEventLag = 100,
        TimeSpan? maxSameLagTime = null
    ) where TStore : class, IDocumentStore
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The settings and the tracker travel with this store's registration rather than through the
        // singletons the default store's check uses, so registering this never re-configures that check
        var settings = new AsyncDaemonHealthCheckSettings(maxEventLag, maxSameLagTime);
        var tracker = new ProjectionStateTracker();

        builder.Services.TryAddSingleton(TimeProvider.System);
        return builder.Add(new HealthCheckRegistration(
            AsyncDaemonHealthCheckNameFor<TStore>(),
            services => new AsyncDaemonHealthCheck(services.GetRequiredService<TStore>(), settings,
                services.GetRequiredService<TimeProvider>(), tracker),
            failureStatus: null,
            tags: new[] { "Marten", "AsyncDaemon", typeof(TStore).Name }
        ));
    }

    /// <summary>
    ///     marten#6189: the name a store's async daemon check is registered under by
    ///     <see cref="AddMartenAsyncDaemonHealthCheck{TStore}" />.
    /// </summary>
    public static string AsyncDaemonHealthCheckNameFor<TStore>() where TStore : class, IDocumentStore =>
        $"{nameof(AsyncDaemonHealthCheck)}:{typeof(TStore).Name}";

    /// <summary>
    ///     Internal class used to DI settings to async daemon health check
    /// </summary>
    /// <param name="MaxEventLag"></param>
    /// <returns></returns>
    public record AsyncDaemonHealthCheckSettings(int MaxEventLag, TimeSpan? MaxSameLagTime = null);

    /// <summary>
    ///     Tracks projection state across multiple health check invocations
    /// </summary>
    public class ProjectionStateTracker
    {
        /// <summary>
        ///     Every shard found lagging on the last check, keyed by database identifier and shard identity, with
        ///     when it was first seen lagging at its current sequence and that sequence
        /// </summary>
        public ConcurrentDictionary<string, (DateTime CheckedAt, long Sequence)> LastProjectionsChecks { get; } = new();
    }

    /// <summary>
    ///     Health check implementation
    /// </summary>
    public class AsyncDaemonHealthCheck: IHealthCheck
    {
        /// <summary>
        ///     The allowed event projection processing lag compared to the HighWaterMark.
        /// </summary>
        private readonly int _maxEventLag;

        /// <summary>
        ///     The allowed time for event projection is lagging (by maxEventLag).
        ///     If not provided every projection is considered lagging if HighWaterMark - projection.Position >= maxEventLag.
        ///     If provided only if projection.Position is still the same for given time.
        ///     When you want to rely only on time just set _maxEventLag=1 and maxSameLagTime to desired value.
        /// </summary>
        private readonly TimeSpan? _maxSameLagTime;

        /// <summary>
        ///     The <see cref="DocumentStore" /> to check health for.
        /// </summary>
        private readonly IDocumentStore _store;

        private readonly TimeProvider _timeProvider;

        /// <summary>
        ///     Projection state tracker that persists between health check invocations
        /// </summary>
        private readonly ProjectionStateTracker _stateTracker;

        public AsyncDaemonHealthCheck(IDocumentStore store, AsyncDaemonHealthCheckSettings settings,
            TimeProvider timeProvider, ProjectionStateTracker stateTracker)
        {
            _store = store;
            _timeProvider = timeProvider;
            _maxEventLag = settings.MaxEventLag;
            _maxSameLagTime = settings.MaxSameLagTime;
            _stateTracker = stateTracker;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                // marten#6189: every shard of every async projection, at its registered version. A per-tenant
                // shard is matched by these same parts, whatever its tenant.
                var projectionsToCheck = _store.Options.Events.Projections()
                    .Where(x => x.Lifecycle == ProjectionLifecycle.Async)
                    .SelectMany(x => x.ShardNames())
                    .Select(x => ShardName.Compose(x.Name, x.ShardKey, version: x.Version).Identity)
                    .ToHashSet();

                var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);

                if (databases.Count == 1)
                {
                    return await CheckProjectionHealthAsync(databases[0], projectionsToCheck, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Cheesy, but just returning unhealthy
                    foreach (var database in databases)
                    {
                        var healthCheck =
                            await CheckProjectionHealthAsync(database, projectionsToCheck, cancellationToken)
                                .ConfigureAwait(false);
                        if (healthCheck.Status != HealthStatus.Healthy)
                        {
                            return healthCheck;
                        }
                    }
                }

                return HealthCheckResult.Healthy("Healthy");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy($"Unhealthy: {ex.Message}", ex);
            }
        }

        public async Task<HealthCheckResult> CheckProjectionHealthAsync(IMartenDatabase database,
            HashSet<string> projectionsToCheck,
            CancellationToken cancellationToken)
        {
            var allProgress = await database.AllProjectionProgress(cancellationToken)
                .ConfigureAwait(true);

            // The store-global mark, and under per-tenant event partitioning one HighWaterMark:{tenant} per tenant
            var highWaterMarks = new Dictionary<string, long>();
            foreach (var progress in allProgress)
            {
                if (ShardName.TryParse(progress.ShardName, out var name) && name!.IsHighWaterMark)
                {
                    highWaterMarks[name.TenantId ?? string.Empty] = progress.Sequence;
                }
            }

            if (highWaterMarks.Count == 0)
            {
                return HealthCheckResult.Healthy("Healthy");
            }

            var projectionsSequences = allProgress.Where(x => isChecked(x.ShardName, projectionsToCheck))
                .Select(x => new { x.ShardName, x.Sequence, HighWaterMark = highWaterMarkFor(x.ShardName, highWaterMarks) })
                .Where(x => x.HighWaterMark.HasValue)
                .ToArray();

            var laggingProjections = projectionsSequences
                .Where(x => x.Sequence <= x.HighWaterMark!.Value - _maxEventLag)
                .ToArray();

            if (_maxSameLagTime is null)
            {
                return laggingProjections.Length != 0
                    ? HealthCheckResult.Unhealthy(
                        $"Unhealthy: Async projection sequence is more than {_maxEventLag} events behind for projection(s): {laggingProjections.Select(x => x.ShardName).Join(", ")}"
                    )
                    : HealthCheckResult.Healthy("Healthy");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var keyPrefix = $"{database.Identifier}:";

            // marten#6189: the timestamp is when the shard was first seen lagging at its current sequence. It is
            // kept while the sequence stays put and reset only when the shard moves, so the check fires once the
            // shard has sat still for maxSameLagTime however often it is probed.
            var projectionsLaggingWithSamePositionForGivenTime = new List<string>();
            foreach (var laggingProjection in laggingProjections)
            {
                var (laggingSince, _) = _stateTracker.LastProjectionsChecks.AddOrUpdate(
                    keyPrefix + laggingProjection.ShardName,
                    _ => (now, laggingProjection.Sequence),
                    (_, existing) => existing.Sequence == laggingProjection.Sequence
                        ? existing
                        : (now, laggingProjection.Sequence)
                );

                if (now.Subtract(laggingSince) >= _maxSameLagTime)
                {
                    projectionsLaggingWithSamePositionForGivenTime.Add(laggingProjection.ShardName);
                }
            }

            // A shard that caught up starts its clock again the next time it lags
            var stillLagging = laggingProjections.Select(x => keyPrefix + x.ShardName).ToHashSet();
            foreach (var key in _stateTracker.LastProjectionsChecks.Keys)
            {
                if (key.StartsWith(keyPrefix, StringComparison.Ordinal) && !stillLagging.Contains(key))
                {
                    _stateTracker.LastProjectionsChecks.TryRemove(key, out _);
                }
            }

            return projectionsLaggingWithSamePositionForGivenTime.Count != 0
                ? HealthCheckResult.Unhealthy(
                    $"Unhealthy: Async projection sequence is more than {_maxEventLag} events behind with same sequence for more than {_maxSameLagTime} for projection(s): {projectionsLaggingWithSamePositionForGivenTime.Join(", ")}"
                )
                : HealthCheckResult.Healthy("Healthy");
        }

        private static long? highWaterMarkFor(string progressionName, Dictionary<string, long> highWaterMarks)
        {
            // A per-tenant shard measures against its own tenant's mark when the store keeps one
            if (ShardName.TryParse(progressionName, out var shardName) && shardName!.TenantId != null &&
                highWaterMarks.TryGetValue(shardName.TenantId, out var tenantMark))
            {
                return tenantMark;
            }

            return highWaterMarks.TryGetValue(string.Empty, out var globalMark) ? globalMark : null;
        }

        private static bool isChecked(string progressionName, HashSet<string> projectionsToCheck)
        {
            if (projectionsToCheck.Contains(progressionName)) return true;

            // A per-tenant shard carries its tenant as a trailing segment
            return ShardName.TryParse(progressionName, out var shardName) && shardName is { IsHighWaterMark: false, TenantId: not null } &&
                   projectionsToCheck.Contains(ShardName.Compose(shardName.Name, shardName.ShardKey, version: shardName.Version).Identity);
        }
    }
}
