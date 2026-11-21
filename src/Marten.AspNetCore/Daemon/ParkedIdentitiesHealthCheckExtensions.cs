using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marten.Events.Daemon;

/// <summary>
///     marten#6122: an ASP.NET Core health check over parked projection identities (marten#6003). It reports
///     <see cref="HealthStatus.Degraded" /> while any identity is parked in any of the store's databases, and
///     <see cref="HealthStatus.Unhealthy" /> while a shard is paused at the parking cap
///     (<see cref="ShardFailureCategory.ParkedLimitReached" />).
/// </summary>
/// <remarks>
///     A parked identity's document is stuck at its last good state until someone replays or discards it, but the
///     rest of the projection keeps going, so that is Degraded. A shard that hit the cap has stopped altogether, so
///     that is Unhealthy. The cap is read from the extended progression columns (on by default in Marten 10), so it
///     works from any node, whichever one runs the shard.
/// </remarks>
public static class ParkedIdentitiesHealthCheckExtensions
{
    /// <summary>
    ///     Register the parked identities health check for the store registered with <c>AddMarten()</c>.
    ///     Remember to add <c>app.MapHealthChecks("/your-health-path")</c> to the middleware pipeline.
    /// </summary>
    public static IHealthChecksBuilder AddMartenParkedIdentitiesHealthCheck(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(nameof(ParkedIdentitiesHealthCheck),
            services => new ParkedIdentitiesHealthCheck(services.GetRequiredService<IDocumentStore>()),
            failureStatus: null,
            tags: new[] { "Marten", "AsyncDaemon", "Parked" }));
    }

    /// <summary>
    ///     Register the parked identities health check for a store registered with
    ///     <c>AddMartenStore&lt;TStore&gt;()</c>. Each store's check is named
    ///     <c>ParkedIdentitiesHealthCheck:&lt;store type&gt;</c>, so it can sit beside the default store's check.
    /// </summary>
    /// <typeparam name="TStore">The store's marker interface, as passed to <c>AddMartenStore&lt;TStore&gt;()</c>.</typeparam>
    public static IHealthChecksBuilder AddMartenParkedIdentitiesHealthCheck<TStore>(this IHealthChecksBuilder builder)
        where TStore : class, IDocumentStore
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration($"{nameof(ParkedIdentitiesHealthCheck)}:{typeof(TStore).Name}",
            services => new ParkedIdentitiesHealthCheck(services.GetRequiredService<TStore>()),
            failureStatus: null,
            tags: new[] { "Marten", "AsyncDaemon", "Parked", typeof(TStore).Name }));
    }

    /// <summary>
    ///     Degraded while any identity is parked, Unhealthy while any shard is paused at the parking cap, in any of
    ///     the store's databases.
    /// </summary>
    public sealed class ParkedIdentitiesHealthCheck: IHealthCheck
    {
        private readonly IDocumentStore _store;

        /// <summary>Construct the check over a store.</summary>
        public ParkedIdentitiesHealthCheck(IDocumentStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var atCap = new List<string>();
                var parked = new List<string>();
                var total = 0;

                foreach (var database in await _store.Storage.AllDatabases().ConfigureAwait(false))
                {
                    var progress = await database.AllProjectionProgress(cancellationToken).ConfigureAwait(false);
                    atCap.AddRange(progress
                        .Where(x => x.Failure?.Category == ShardFailureCategory.ParkedLimitReached)
                        .Select(x => x.ShardName));

                    foreach (var count in await ((IEventDatabase)database)
                                 .FetchParkedCountsAsync(null, cancellationToken).ConfigureAwait(false))
                    {
                        total += count.Count;
                        parked.Add($"{count.ProjectionName} (tenant {count.TenantId}): {count.Count}");
                    }
                }

                if (atCap.Count > 0)
                {
                    return HealthCheckResult.Unhealthy(
                        $"Paused at the parking cap: {string.Join(", ", atCap)}. Fix the projection, replay or discard what's parked, then restart the shard.");
                }

                return total == 0
                    ? HealthCheckResult.Healthy("No projection identities are parked.")
                    : HealthCheckResult.Degraded(
                        $"{total} projection {(total == 1 ? "identity is" : "identities are")} parked: {string.Join(", ", parked)}");
            }
            catch (Exception e)
            {
                return HealthCheckResult.Unhealthy($"Unhealthy: {e.Message}", e);
            }
        }
    }
}
