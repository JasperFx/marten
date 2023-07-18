# Async Daemon HealthChecks

Marten supports a customizable [HealthChecks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-7.0). This can be useful when running the async daemon in a containerized environment such as Kubernetes. The check will verify that no projection's progression lags more than `maxEventLag` behind the `HighWaterMark`. The default `maxEventLag` is 100.

> The healthcheck will only be checked against `Async` projections

<!-- snippet: sample_AddMartenAsyncDaemonHealthCheck -->
<a id='sample_addmartenasyncdaemonhealthcheck'></a>
```cs
// Add HealthCheck
Services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck(maxEventLag: 500);

// Map HealthCheck Endpoint
app.MapHealthChecks("/health");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Daemon/AsyncDaemonHealthCheckExtensions.cs#L62-L95' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenasyncdaemonhealthcheck' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
