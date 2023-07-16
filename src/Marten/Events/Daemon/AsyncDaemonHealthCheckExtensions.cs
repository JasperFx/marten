#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marten.Events.Daemon;

public static class AsyncDaemonHealthCheckExtensions
{
    /// <summary>
    /// Adds a health check for Martens Async Daemon. 
    /// The health check will verify that no async projection progression is lagging behind more than the <paramref name="maxEventLag"/>
    /// The check will return <see cref="HealthCheckResult.Unhealthy"/> if any progression is more than <paramref name="maxEventLag"/> behind the highWaterMark OR if any exception is thrown while doing the check.
    /// <example> 
    /// <code>
    /// Customized Injection Example: services.AddHealthChecks().AddAsyncDaemonHealthCheck(150);
    /// </code>
    /// </example>
    /// Also - remember to add <c>app.MapHealthChecks("/your-health-path")</c> to the middleware pipeline
    /// </summary>
    /// <param name="builder"><see cref="IHealthChecksBuilder"/></param>
    /// <param name="maxEventLag">(optional) Acceptable lag of an eventprojection before it's considered unhealthy - defaults to 100</param>
    /// <returns>If healthy: <see cref="HealthCheckResult.Healthy"/> - else <see cref="HealthCheckResult.Unhealthy"/></returns>
    public static IHealthChecksBuilder AddMartenAsyncDaemonHealthCheck(this IHealthChecksBuilder builder, int maxEventLag = 100)
    {
        builder.Services.AddSingleton(new AsyncDaemonHealthCheckSettings(maxEventLag));
        return builder.AddCheck<AsyncDaemonHealthCheck>(nameof(AsyncDaemonHealthCheck), tags: new[] {"Marten", "AsyncDaemon"});
    }

    /// <summary>
    /// Internal class used to DI settings to async daemon health check
    /// </summary>
    /// <param name="MaxEventLag"></param>
    /// <returns></returns>
    internal record AsyncDaemonHealthCheckSettings(int MaxEventLag);

    /// <summary>
    /// Health check implementation
    /// </summary>
    internal class AsyncDaemonHealthCheck : IHealthCheck
    {
        /// <summary>
        /// The <see cref="DocumentStore"/> to check health for.
        /// </summary>
        private IDocumentStore _store;

        /// <summary>
        /// The allowed event projection processing lag compared to the HighWaterMark.
        /// </summary>
        private int _maxEventLag;

        internal AsyncDaemonHealthCheck(IDocumentStore store, AsyncDaemonHealthCheckSettings settings)
        {
            _store = store;
            _maxEventLag = settings.MaxEventLag;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
                                                              CancellationToken cancellationToken = default)
        {
            try
            {
                var projectionsToCheck = _store.Options
                                               .Events
                                               .Projections()
                                               .Where(x => x.Lifecycle == ProjectionLifecycle.Async) // Only check async projections to avoid issus where inline progression counter is set.
                                               .Select(x => $"{x.ProjectionName}:All")
                                               .ToHashSet();

                var allProgress = await _store.Advanced.AllProjectionProgress(token: cancellationToken).ConfigureAwait(true);

                var highWaterMark = allProgress.First(x => string.Equals("HighWaterMark", x.ShardName));
                var projectionMarks = allProgress.Where(x => !string.Equals("HighWaterMark", x.ShardName));

                var unhealthy = projectionMarks
                                .Where(x => projectionsToCheck.Contains(x.ShardName))
                                .Where(x => x.Sequence <= highWaterMark.Sequence - _maxEventLag)
                                .Select(x => x.ShardName)
                                .ToArray();

                return unhealthy.Any()
                  ? HealthCheckResult.Unhealthy($"Unhealthy: Async projection sequence is more than {_maxEventLag} events behind for projection(s): {unhealthy.Join(", ")}")
                  : HealthCheckResult.Healthy("Healthy");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy($"Unhealthy: {ex.Message}", ex);
            }
        }
    }
}
