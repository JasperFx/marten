# Async Daemon HealthChecks

Marten supports a customizable [HealthChecks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-7.0). This can be useful when running the async daemon in a containerized environment such as Kubernetes. The check will verify that no projection's progression lags more than `maxEventLag` behind the `HighWaterMark`. The default `maxEventLag` is 100.  

The `maxEventLag` setting controls how far behind the `HighWaterMark` any async projection is allowed to lag before it's considered unhealthy. E.g. if the `HighWaterMark` is 1000 and an a system with 3 async projections `ProjA`, `ProjB` and `ProjC` are processed respectively to sequence number 899, 901 and 901 then the system will be considered unhealthy with a `maxEventLag` of 100 (1000 - 899 = 101), BUT healthy with a `mavEventLag` of 101 or higher.

> NOTE: The healthcheck will only be checked against `Async` projections

### Example configuration:

```cs
// Add HealthCheck
Services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck(maxEventLag: 500);

// Map HealthCheck Endpoint
app.MapHealthChecks("/health");
```
