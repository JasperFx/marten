# Async Daemon HealthChecks

::: tip INFO
The healthcheck is available in the [Marten.AspNetCore](https://www.nuget.org/packages/Marten.AspNetCore) package.
:::

Marten supports a customizable [HealthChecks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-7.0). 
This can be useful when running the async daemon in a containerized environment such as Kubernetes. 
Especially if you experience `ProgressionProgressOutOfOrderException` errors in async projections.

The check will verify that no projection's progression lags more than `maxEventLag` behind the `HighWaterMark`. 
The default `maxEventLag` is 100. Read more about events progression tracking and `HighWaterMark` in [Async Daemon documentation](/events/projections/async-daemon).

The `maxEventLag` setting controls how far behind the `HighWaterMark` any async projection is allowed to lag before it's considered unhealthy. 
E.g. if the `HighWaterMark` is 1000 and an a system with 3 async projections `ProjA`, `ProjB` and `ProjC` are processed respectively to sequence number 899, 901 and 901 then the system will be considered unhealthy with a `maxEventLag` of 100 (1000 - 899 = 101), BUT healthy with a `mavEventLag` of 101 or higher.

::: tip INFO
The healthcheck will only be checked against `Async` projections
:::

## Example configuration:

```cs
// Add HealthCheck
Services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck(maxEventLag: 500);

// Map HealthCheck Endpoint
app.MapHealthChecks("/health");
```

If you want to add some time toleration for the healthcheck, you may use additional parameter `maxSameLagTime`.
It treats as unhealthy projections same as described below, but ONLY IF the same projection lag remains for the given time.

### Example use case #1 
Assuming that `maxEventLag` = `100` and `maxSameLagTime` = `TimeSpan.FromSeconds(30)`:
- `HighWaterMark` is 1000 and async projection was processed to sequence number 850 at 2024-02-07 01:30:00 -> 'Healthy' 
- `HighWaterMark` is 1000 and async projection was processed to sequence number 850 at 2024-02-07 01:30:30 -> 'Unhealthy' 

It's unhealty, because the projection haven't progressed since last healthcheck and `maxSameLagTime` elapsed on the same sequence number.


### Example use case #2 
Assuming that `maxEventLag` = `100` and `maxSameLagTime` = `TimeSpan.FromSeconds(30)`:
- `HighWaterMark` is 1000 and async projection was processed to sequence number 850 at 2024-02-07 01:30:00 -> 'Healthy'
- `HighWaterMark` is 1000 and async projection was processed to sequence number 851 at 2024-02-07 01:30:30 -> 'Healthy'

It's healthy, because the projection progressed since last healthcheck.

## Example configuration:

```cs
// Add HealthCheck
Services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck(maxEventLag: 500, maxSameLagTime: TimeSpan.FromSeconds(30));

// Map HealthCheck Endpoint
app.MapHealthChecks("/health");
```
