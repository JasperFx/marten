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

It's unhealthy, because the projection haven't progressed since last healthcheck and `maxSameLagTime` elapsed on the same sequence number.

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

## High-Water Staleness HealthCheck

`AddMartenAsyncDaemonHealthCheck` measures each async projection's lag *against* the
`HighWaterMark`. That makes it blind to a dead or wedged high-water agent: when the agent
stops, the mark freezes, every projection catches up to that frozen value, the lag drops to
zero, and the check reports **Healthy** — the exact inverse of what you want.

`AddMartenHighWaterHealthCheck` closes that gap. Instead of comparing projections to the
mark, it compares the `HighWaterMark` itself against the actual highest event sequence, and
reports **Unhealthy** only when the mark *stops advancing* while later events keep piling up
past it — a strong signal that the high-water agent has died (see
[marten#4961](https://github.com/JasperFx/marten/issues/4961)).

```cs
// Add HealthCheck
Services.AddHealthChecks().AddMartenHighWaterHealthCheck(staleThreshold: TimeSpan.FromSeconds(30));

// Map HealthCheck Endpoint
app.MapHealthChecks("/health");
```

The check is deliberately conservative to avoid false positives:

- **Gating.** It reports Healthy (not applicable) unless the store actually runs the daemon —
  it needs at least one `Async` projection or subscription registered, and an `AsyncMode` of
  `Solo` or `HotCold`. `Disabled` and `ExternallyManaged` stores never assert on the mark.
- **Sustained non-advance only.** A non-zero gap between the highest event sequence and the
  mark is *normal* transiently (the detector deliberately holds the mark inside a "safe
  harbor" behind in-flight or gapped sequences). The check therefore trips only when the mark
  sits at the same value for at least `staleThreshold` (default 30 seconds) while events remain
  unprocessed beyond it — never on a single snapshot.
- **Store-global reading.** It reads database state, so it is correct on any node regardless of
  which one runs the agent; it fails only when *no* node is advancing the mark.

::: tip INFO
This health check does **not** force the high-water mark forward — it is detection only.
:::
