using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
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
        return builder.AddCheck<AsyncDaemonHealthCheck>(
            nameof(AsyncDaemonHealthCheck),
            tags: new[] { "Marten", "AsyncDaemon" }
        );
    }

    /// <summary>
    ///     Internal class used to DI settings to async daemon health check
    /// </summary>
    /// <param name="MaxEventLag"></param>
    /// <returns></returns>
    internal record AsyncDaemonHealthCheckSettings(int MaxEventLag, TimeSpan? MaxSameLagTime = null);

    /// <summary>
    ///     Health check implementation
    /// </summary>
    internal class AsyncDaemonHealthCheck: IHealthCheck
    {
        private readonly ConcurrentDictionary<string, (DateTime CheckedAt, long Sequence)>
            _lastProjectionsChecks = new();

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

        public AsyncDaemonHealthCheck(IDocumentStore store, AsyncDaemonHealthCheckSettings settings,
            TimeProvider timeProvider)
        {
            _store = store;
            _timeProvider = timeProvider;
            _maxEventLag = settings.MaxEventLag;
            _maxSameLagTime = settings.MaxSameLagTime;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                var projectionsToCheck = _store.Options.Events.Projections()
                    .Where(x => x.Lifecycle == ProjectionLifecycle.Async)
                    .Select(x => $"{x.Name}:All")
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

            var highWaterMark = allProgress.FirstOrDefault(x => string.Equals("HighWaterMark", x.ShardName));
            if (highWaterMark is null)
            {
                return HealthCheckResult.Healthy("Healthy");
            }

            var projectionMarks = allProgress.Where(x => !string.Equals("HighWaterMark", x.ShardName)).ToArray();

            var projectionsSequences = projectionMarks.Where(x => projectionsToCheck.Contains(x.ShardName))
                .Select(x => new { x.ShardName, x.Sequence })
                .ToArray();

            var laggingProjections = projectionsSequences
                .Where(x => x.Sequence <= highWaterMark.Sequence - _maxEventLag)
                .ToArray();

            if (_maxSameLagTime is null)
            {
                return laggingProjections.Any()
                    ? HealthCheckResult.Unhealthy(
                        $"Unhealthy: Async projection sequence is more than {_maxEventLag} events behind for projection(s): {laggingProjections.Select(x => x.ShardName).Join(", ")}"
                    )
                    : HealthCheckResult.Healthy("Healthy");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var projectionsLaggingWithSamePositionForGivenTime = laggingProjections.Where(
                    x =>
                    {
                        var (laggingSince, lastKnownPosition) =
                            _lastProjectionsChecks.GetValueOrDefault(x.ShardName, (now, x.Sequence));

                        var isLaggingWithSamePositionForGivenTime =
                            now.Subtract(laggingSince) >= _maxSameLagTime &&
                            x.Sequence == lastKnownPosition;

                        return isLaggingWithSamePositionForGivenTime;
                    }
                )
                .ToArray();

            foreach (var laggingProjection in laggingProjections)
            {
                _lastProjectionsChecks.AddOrUpdate(
                    laggingProjection.ShardName,
                    _ => (now, laggingProjection.Sequence),
                    (_, _) => (now, laggingProjection.Sequence)
                );
            }

            return projectionsLaggingWithSamePositionForGivenTime.Any()
                ? HealthCheckResult.Unhealthy(
                    $"Unhealthy: Async projection sequence is more than {_maxEventLag} events behind with same sequence for more than {_maxSameLagTime} for projection(s): {projectionsLaggingWithSamePositionForGivenTime.Select(x => x.ShardName).Join(", ")}"
                )
                : HealthCheckResult.Healthy("Healthy");
        }
    }
}
