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
E.g. if the `HighWaterMark` is 1000 and a system with 3 async projections `ProjA`, `ProjB` and `ProjC` are processed respectively to sequence number 899, 901 and 901 then the system will be considered unhealthy with a `maxEventLag` of 100 (1000 - 899 = 101), BUT healthy with a `maxEventLag` of 101 or higher.

::: tip INFO
The healthcheck will only be checked against `Async` projections
:::

## Example configuration

<!-- snippet: sample_async_daemon_health_check -->
<a id='snippet-sample_async_daemon_health_check'></a>
```cs
// Add HealthCheck
builder.Services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck(maxEventLag: 500);

// Map HealthCheck Endpoint
var app = builder.Build();
app.MapHealthChecks("/health");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/health_check_documentation_samples.cs#L25-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_daemon_health_check' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you want to add some time toleration for the healthcheck, you may use additional parameter `maxSameLagTime`.
A projection is then unhealthy only if it stays behind by more than `maxEventLag` at the same sequence for
`maxSameLagTime`.

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

## Example configuration with `maxSameLagTime`

<!-- snippet: sample_async_daemon_health_check_with_same_lag_time -->
<a id='snippet-sample_async_daemon_health_check_with_same_lag_time'></a>
```cs
// Add HealthCheck
builder.Services.AddHealthChecks()
    .AddMartenAsyncDaemonHealthCheck(maxEventLag: 500, maxSameLagTime: TimeSpan.FromSeconds(30));

// Map HealthCheck Endpoint
var app = builder.Build();
app.MapHealthChecks("/health");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/health_check_documentation_samples.cs#L41-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_daemon_health_check_with_same_lag_time' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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

<!-- snippet: sample_high_water_health_check -->
<a id='snippet-sample_high_water_health_check'></a>
```cs
// Add HealthCheck
builder.Services.AddHealthChecks().AddMartenHighWaterHealthCheck(staleThreshold: TimeSpan.FromSeconds(30));

// Map HealthCheck Endpoint
var app = builder.Build();
app.MapHealthChecks("/health");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/health_check_documentation_samples.cs#L58-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_high_water_health_check' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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

### Separate stores

`AddMartenHighWaterHealthCheck()` checks the default store registered with `AddMarten()`. For a
store registered with `AddMartenStore<TStore>()`, use the generic overload. It probes that store,
and with `autoRestart` it restarts the agent through that store's own projection coordinator
(see [marten#5705](https://github.com/JasperFx/marten/issues/5705)):

<!-- snippet: sample_high_water_health_check_for_a_separate_store -->
<a id='snippet-sample_high_water_health_check_for_a_separate_store'></a>
```cs
builder.Services.AddMartenStore<IInvoicingStore>(opts =>
{
    // ...
}).AddAsyncDaemon(DaemonMode.Solo);

builder.Services.AddHealthChecks()
    .AddMartenHighWaterHealthCheck<IInvoicingStore>(
        staleThreshold: TimeSpan.FromSeconds(30),
        autoRestart: true);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/health_check_documentation_samples.cs#L74-L86' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_high_water_health_check_for_a_separate_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Call it once per store. Each store's check is registered under its own name,
`HighWaterHealthCheck:IInvoicingStore`, with its own settings, so it can sit beside the default
store's check. Every option below works the same way on the generic overload.

### Sharded and multi-tenant stores

On a multi-database store (`MultiTenantedWithShardedDatabases`, or any store with more than one
tenant database) the check probes **every** database by default. With hundreds of shard
databases that is a connection fan-out on each health probe, and — when daemon distribution is
spread across nodes (e.g. Wolverine-managed) — a node ends up probing databases it does not host
the daemon for. Two options scope it to the databases the local node actually owns
(see [marten#4991](https://github.com/JasperFx/marten/issues/4991)):

<!-- snippet: sample_high_water_health_check_for_locally_owned_databases -->
<a id='snippet-sample_high_water_health_check_for_locally_owned_databases'></a>
```cs
builder.Services.AddHealthChecks().AddMartenHighWaterHealthCheck(
    staleThreshold: TimeSpan.FromSeconds(30),

    // Only probe (and, with autoRestart, only restart) the databases this node owns. The
    // predicate receives each IMartenDatabase; return true for the ones the local node hosts.
    databaseFilter: db => LocallyOwnedDatabaseIdentifiers.Contains(db.Identifier),

    // Assert even under DaemonMode.ExternallyManaged (Wolverine-managed distribution). In this
    // mode only the per-tenant poll-cycle signal is used (never the store-global sequence-gap
    // heuristic), because an external owner can legitimately pause the mark — so a non-partitioned
    // store has nothing to assert on there.
    includeExternallyManaged: true);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/health_check_documentation_samples.cs#L93-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_high_water_health_check_for_locally_owned_databases' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### When ownership is runtime state

That `databaseFilter` closure is captured at registration time, so it can only read what the
application already has in hand — an ambient collection the app keeps up to date, a config value, a
static. It cannot resolve services.

Under **Wolverine-managed daemon distribution** that is not enough: which databases a node owns is
decided by Wolverine's agent assignments and moves over the node's lifetime (rollouts, node loss,
leadership changes), and the authoritative answer has to be read on *each* probe. Use the overload
whose filter takes the `IServiceProvider` — it is invoked once per database per probe
(see [marten#5061](https://github.com/JasperFx/marten/issues/5061)):

```cs
Services.AddHealthChecks().AddMartenHighWaterHealthCheck(
    // Re-evaluated on every probe, against the provider the health check itself was resolved
    // from — so scoped services are legal too.
    (services, database) => services.GetRequiredService<IWolverineRuntime>()
        .Agents.AllLocallyOwnedDatabaseIds()
        .Any(id => id.Name.EqualsIgnoreCase(database.Identifier)),

    staleThreshold: TimeSpan.FromSeconds(30),
    includeExternallyManaged: true);
```

Both overloads register the settings through a factory, so replacing
`HighWaterHealthCheckSettings` in the container is also a supported way to override the filter if
you need to reach further into DI than the predicate allows. That applies to the default store's
check only: a separate store's check carries its settings with its own registration.

Under `UseTenantPartitionedEvents` the high-water mark is tracked **per tenant** as
`HighWaterMark:<tenant>` progression rows rather than a single store-global `HighWaterMark`, and
those are the rows the check evaluates. It uses a different — and better — signal there: every
vectorized per-tenant poll re-stamps the row's `last_updated` column whether or not the mark
advances, so its age proves the poll loop is still *cycling*, independent of whether new events
exist. A quiet tenant never false-positives, and the sequence-gap heuristic (which is inherently
store-global) is never applied per tenant. No extra configuration is needed —
`EnableExtendedProgressionTracking` is not involved
(see [marten#5174](https://github.com/JasperFx/marten/issues/5174)).

::: warning
On the **store-global** `HighWaterMark` row `last_updated` only moves when the mark *advances*, so
it says nothing about liveness and the sequence-gap heuristic is the only signal available from the
database. That means a store-global poll loop that has wedged while fully caught up is not
detectable from the progression table. For in-process detection of that case, use `IProjectionDaemon.IsHighWaterStale`
and `HighWaterLastPolledAt` on the node hosting the daemon.
:::

::: tip INFO
This health check does **not** force the high-water mark forward — it is detection only.
`autoRestart: true` may additionally ask the local coordinator to restart the high-water poll
loop, which also never advances the mark.
:::

## Parked Identities HealthCheck <Badge type="tip" text="10.0" /> {#parked-identities-healthcheck}

When an async projection [parks a failing identity](/events/projections/async-daemon#parking-a-failing-identity), the
rest of the projection keeps going, so neither check above notices. `AddMartenParkedIdentitiesHealthCheck()` does:

<!-- snippet: sample_registering_the_parked_identities_health_check -->
<a id='snippet-sample_registering_the_parked_identities_health_check'></a>
```cs
services.AddHealthChecks().AddMartenParkedIdentitiesHealthCheck();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/parked_identities_health_check.cs#L109-L113' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_the_parked_identities_health_check' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Map it with `app.MapHealthChecks("/health")` like the others. It reports:

- **Degraded** while any identity is parked in any of the store's databases, naming each projection and tenant with its
  count, then the first 10 parked identities of each projection. A parked document is stuck at its last good state until
  someone replays or discards it, but everything else is still moving.
- **Unhealthy** while a shard is paused at the parking cap (`ShardFailureCategory.ParkedLimitReached`), which means the
  whole projection has stopped. Fix the projection, replay or discard what's parked, then restart the shard. The cap
  (`MaxParkedIdentitiesPerShard`) and the systemic trip that pauses a batch parking too many identities at once are
  described in [Guard rails](https://shared-libs.jasperfx.net/events/parked-identities#guard-rails).

Each listed identity is the stream id, followed by the aggregate's [natural key](/events/natural-keys) in parentheses
when it has one. A projection with more than 10 parked ends its list with "and N more". A Degraded description reads
something like this:

```txt
2 projection identities are parked: Ledger (tenant *DEFAULT*): 2. Parked identities: Ledger:
13f89f60-b7e7-412e-8899-d86babf2ca23 (L-100), 561a1a40-89d4-48fc-b5f3-cc2d0a6f33a3
```

The same lists are in the result's `Data`, keyed by projection name:

<!-- snippet: sample_parked_identities_in_the_health_check_data -->
<a id='snippet-sample_parked_identities_in_the_health_check_data'></a>
```cs
// The first ten parked identities of each projection, as "id (natural key)" when there's a key
var listed = (string[])degraded.Data["ParkedHealthScale"];
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/parked_identities_health_check.cs#L198-L203' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_parked_identities_in_the_health_check_data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The check reads the database, so it gives the same answer on every node, whichever one runs the shard. The parking cap
is read from the extended progression columns, which are on by default in Marten 10.

For a store registered with `AddMartenStore<TStore>()`, use `AddMartenParkedIdentitiesHealthCheck<TStore>()`. That check
is named `ParkedIdentitiesHealthCheck:<store type>`.
