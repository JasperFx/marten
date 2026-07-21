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
  `Solo` or `HotCold`. `Disabled` stores never assert on the mark. `ExternallyManaged` stores
  (for example Wolverine-managed daemon distribution) also skip by default, but you can opt in
  with `includeExternallyManaged: true` (see below).
- **Sustained non-advance only.** A non-zero gap between the highest event sequence and the
  mark is *normal* transiently (the detector deliberately holds the mark inside a "safe
  harbor" behind in-flight or gapped sequences). The check therefore trips only when the mark
  sits at the same value for at least `staleThreshold` (default 30 seconds) while events remain
  unprocessed beyond it — never on a single snapshot.
- **Store-global reading.** It reads database state, so it is correct on any node regardless of
  which one runs the agent; it fails only when *no* node is advancing the mark.

### Sharded and multi-tenant stores

On a multi-database store (`MultiTenantedWithShardedDatabases`, or any store with more than one
tenant database) the check probes **every** database by default. With hundreds of shard
databases that is a connection fan-out on each health probe, and — when daemon distribution is
spread across nodes (e.g. Wolverine-managed) — a node ends up probing databases it does not host
the daemon for. Two options scope it to the databases the local node actually owns
(see [marten#4991](https://github.com/JasperFx/marten/issues/4991)):

```cs
Services.AddHealthChecks().AddMartenHighWaterHealthCheck(
    staleThreshold: TimeSpan.FromSeconds(30),

    // Only probe (and, with autoRestart, only restart) the databases this node owns. The
    // predicate receives each IMartenDatabase; return true for the ones the local node hosts.
    databaseFilter: db => LocallyOwnedDatabaseIdentifiers.Contains(db.Identifier),

    // Assert even under DaemonMode.ExternallyManaged (Wolverine-managed distribution). In this
    // mode only the liveness heartbeat signal is used (never the sequence-gap fallback), because
    // an external owner can legitimately pause the mark — so this requires
    // Events.EnableExtendedProgressionTracking to be turned on.
    includeExternallyManaged: true);
```

Under `UseTenantPartitionedEvents` the high-water mark is tracked **per tenant** as
`HighWaterMark:<tenant>` progression rows rather than a single store-global `HighWaterMark`. The
check evaluates those per-tenant rows too, using the liveness heartbeat (the sequence-gap
fallback is store-global and cannot be applied per tenant). Enable
`Events.EnableExtendedProgressionTracking` so the per-tenant heartbeats are persisted, otherwise
a stalled per-tenant high-water agent cannot be detected.

::: tip INFO
This health check does **not** force the high-water mark forward — it is detection only.
`autoRestart: true` may additionally ask the local coordinator to restart the high-water poll
loop, which also never advances the mark.
:::
