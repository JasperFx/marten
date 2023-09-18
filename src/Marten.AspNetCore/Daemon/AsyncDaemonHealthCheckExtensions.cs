#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Marten.Events.Daemon.Internal;

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
        return builder.AddCheck<AsyncDaemonHealthCheck>(nameof(AsyncDaemonHealthCheck), tags: new[] { "Marten", "AsyncDaemon" });
    }
}
