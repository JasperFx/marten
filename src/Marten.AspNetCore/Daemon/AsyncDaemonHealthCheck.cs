#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Projections;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marten.Events.Daemon.Internal;

/// <summary>
/// Health check implementation
/// </summary>
public class AsyncDaemonHealthCheck: IHealthCheck
{
    /// <summary>
    /// The <see cref="DocumentStore"/> to check health for.
    /// </summary>
    private readonly IDocumentStore _store;

    /// <summary>
    /// The allowed event projection processing lag compared to the HighWaterMark.
    /// </summary>
    private readonly int _maxEventLag;

    public AsyncDaemonHealthCheck(IDocumentStore store, AsyncDaemonHealthCheckSettings settings)
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
