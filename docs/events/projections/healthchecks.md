# Async Daemon HealthChecks

Marten supports a customizable [HealthChecks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-7.0). This can be useful when running the async daemon in a containerized environment such as Kubernetes. The check will verify that no projection's progression lags more than `maxEventLag` behind the `HighWaterMark`. The default `maxEventLag` is 100.

> The healthcheck will only be checked against `Async` projections

<!-- snippet: sample_AddMartenAsyncDaemonHealthCheck -->
```cs
// Add HealthCheck
Services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck(maxEventLag: 500);

// Map HealthCheck Endpoint
app.MapHealthChecks("/health");
```
<!-- endSnippet -->
