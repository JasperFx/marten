# Marten.ScaleTesting

Long-running load test harness for Marten 9's async daemon projection rebuilds
([#4666](https://github.com/JasperFx/marten/issues/4666)). Interactive / dev-box
only — **not** packaged, **not** wired into CI.

Drives the daemon-thread-safety synthesis fixes (#4657 → #4658 → #4667 phases)
against realistic conjoined-multi-tenant event interleaving at the 20M+ event
scale, beyond what unit-test fixtures cover.

## Usage

```bash
# Seed N tenants × M events per tenant under conjoined multi-tenancy
dotnet run --project src/Marten.ScaleTesting -- seed --tenants 50 --events-per-tenant 400000 --buckets 8 --seed 42

# Rebuild the TeleHealth composite projection against the seeded data
# (Phase B — not yet implemented)
dotnet run --project src/Marten.ScaleTesting -- rebuild --projection composite --report metrics.json

# Validate aggregates against a single-shard baseline (Phase C — not yet implemented)
dotnet run --project src/Marten.ScaleTesting -- validate --baseline baseline.json
```

Connect string defaults to the standard Marten test connection
(`Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres`).
Override with the `marten_testing_database` env var.

## Project layout

| Directory | Contents |
|---|---|
| `Domain/` | TeleHealth events / aggregates / reference data **lifted** (copied) from `src/DaemonTests/TeleHealth/`. Self-contained so we can extend without touching test fixtures. |
| `Seeding/` | Producer-consumer event seeder: per-stream `IEnumerable<EventBatch>` generators, weighted-random k-way merge, `Channel<EventBatch>` pipeline. |
| `Commands/` | JasperFx.CommandLine subcommands. Modelled on `src/EventAppenderPerfTester/`. |
| `Instrumentation/` | `--instrument`-gated rebuild observability (#4684 Phase E.1+E.2) — `RebuildInstrumentation`, `ProgressSampler`, `NpgsqlCommandCounter`, `ProgressionLockSampler`. No-op when off, zero overhead. |

## Phases

* **Phase A:** project scaffold + lifted Telehealth domain + event seeder + `seed` subcommand. Idempotent via `mt_events` row count check.
* **Phase B:** 4+2+2 composite topology (stage 1: single-stream snapshots + `AppointmentMetricsProjection`; stage 2: `AppointmentDetailsProjection` + `BoardSummaryProjection`; stage 3: NEW `ProviderUtilizationProjection` + `TenantDailyRollupProjection`) + `rebuild` subcommand on the single-pass `CompositeReplayExecutor` path.
* **Phase C:** `validate` (single-shard baseline diff) + `stress` (chain `seed` + `rebuild` + `validate`) + JSON metrics sink.
* **Phase D:** use it. Drive the daemon-thread-safety fixes against the harness — each fix should hold the crash gate AND not regress rebuild time.
* **Phase E.1:** per-period throughput sampling + Npgsql round-trip counting via `--instrument`. Surfaces p50/p95/p99/max throughput + total command count in the console summary and `--metrics` JSON; per-sample CSV trace under `--instrument-trace`.
* **Phase E.2:** progression-row lock-wait sampler. `pg_stat_activity` joined to `pg_locks` at each interval, counting ungranted lock holders on `mt_event_progression` that aren't our own session. Surfaces `MaxConcurrentWaiters`, `MaxSingleWaitMs`, and the approximation `ObservedWaiterSeconds = sum(waiter_count_per_sample) * sample_interval`. Per-sample CSV trace under `--instrument-lock-trace`.
* **Phase E.3 (#4684 remainder):** per-batch wall-clock breakdown + per-batch DB round-trip attribution + lookup counting. The daemon (JasperFx.Events `SubscriptionMetrics`) already emits three spans per event page on the store's `Marten` ActivitySource — `.page.loading` (event fetch), `.page.grouping` (slicing/enrichment) and `.page.execution` (user code + operation building + batch flush) — but only when something listens. `BatchSpanSampler` is that listener under `--instrument`: it stitches the three spans per `(shard, floor)` into one `BatchRecord`, and attributes Npgsql command spans to their enclosing page span by walking the Activity parent chain, giving true per-batch round-trip counts. Output: `perBatch.p50/p95/p99` (total / eventFetch / grouping / execution ms + roundTripCount) plus a `perShard` roll-up in `metrics.json`; per-batch CSV under `--instrument-batch-trace`. `LookupCounters` (item 4) counts the explicit cross-aggregate lookups in the harness projections' own code (`AppointmentMetricsProjection`'s per-specialty `LoadAsync`, `AppointmentDetailsProjection`'s `EnrichUsingEntityQuery` escape hatch) as lookups-per-event distributions per projection. Still blocked on JasperFx-side seams and deliberately NOT approximated here: `RecentlyUsedCache` hit/miss counters (`AggregationRunner.CacheFor` hard-constructs the cache; no injection point) and per-stage timing inside a single composite batch (the composite executor runs stages within one `.page.execution` span; the declarative `EnrichWith<T>().AddReferences()` lookups likewise run inside JasperFx.Events and show up only in the per-batch `grouping`/`execution` round-trip counts).
* **#4683 Fix 3 (this PR):** `dropcycle` subcommand. Exercises the drop-tenant cleanup (#4683) end-to-end against an isolated `UseTenantPartitionedEvents = true` store: registers N tenants, seeds events to populate the per-tenant sequence + progression rows, drops one, asserts the per-tenant artifacts are gone and peers + store-global rows survive. Optionally re-adds + re-seeds and verifies the sequence starts fresh at zero. 14 boolean checks; non-zero exit on any failure. Run via `dotnet run --project src/Marten.ScaleTesting -- dropcycle`.

## Measuring a rebuild

Add `--instrument` to either subcommand to enable per-period sampling against `mt_event_progression`, Npgsql round-trip counting, (E.2) `pg_stat_activity`-based progression lock-wait sampling, and (E.3) the per-batch span sampler + lookup counters. Default 1s sample interval; tune with `--instrument-sample-seconds`. The console summary picks up seven extra rows (throughput percentiles + total Npgsql commands + sample count + three lock-wait counters). `--metrics <path>` writes a JSON file with the rolled-up percentiles + lock-wait stats; `--instrument-trace <path>` adds per-sample progress CSV; `--instrument-lock-trace <path>` adds per-sample lock-wait CSV.

```bash
# Instrumented rebuild against existing seeded data, JSON + CSV output
dotnet run --project src/Marten.ScaleTesting -- rebuild \
    --instrument \
    --instrument-sample-seconds 1.0 \
    --instrument-trace rebuild-trace.csv \
    --instrument-lock-trace rebuild-lock-trace.csv \
    --metrics rebuild-metrics.json

# Chained stress run with instrumentation on the rebuild phase only
dotnet run --project src/Marten.ScaleTesting -- stress \
    --wipe --tenants 50 --events-per-tenant 400000 --writers 8 \
    --shard-timeout-seconds 3600 --baseline scaletest-baseline.json \
    --instrument --instrument-trace stress-trace.csv \
    --instrument-lock-trace stress-lock-trace.csv

# WS2 (jasperfx#486): RUNNING daemon over 100 partitioned tenants under continuous
# append load, sampling pg_stat_activity for the store's connection footprint
dotnet run --project src/Marten.ScaleTesting -- daemonload \
    --tenants 100 --projections 2 --duration-seconds 120 --wipe \
    --metrics daemonload-metrics.json --trace daemonload-connections.csv
```

`metrics.json` shape:

```json
{
  "projection": "rebuild",
  "events": 22610,
  "streams": 2980,
  "elapsedSeconds": 2.6,
  "throughputEventsPerSecond": 8685,
  "instrumentation": {
    "enabled": true,
    "sampleCount": 6,
    "npgsqlCommandCount": 3876,
    "throughput": { "P50": 9986, "P95": 10965, "P99": 10965, "Mean": 8972, "Max": 10965 },
    "progressionLockWaits": {
      "SampleCount": 6,
      "MaxConcurrentWaiters": 0,
      "MaxSingleWaitMs": 0,
      "ObservedWaiterSeconds": 0.0
    },
    "perBatch": {
      "batches": 23,
      "p50": { "TotalMs": 67.6, "EventFetchMs": 7.4, "GroupingMs": 0.3, "ExecutionMs": 58.8, "RoundTripCount": 31, "Events": 500 },
      "p95": { "...": "..." },
      "p99": { "...": "..." },
      "perShard": {
        "marten.telehealthcomposite.all": { "Batches": 23, "Events": 11100, "TotalMsP50": 67.6, "MeanEventsPerBatch": 482.6, "MeanRoundTripsPerBatch": 29.7 }
      }
    },
    "lookups": {
      "AppointmentMetricsProjection": { "Invocations": 115, "Lookups": 636, "Events": 1075, "LookupsPerEvent": 0.59, "PerEventP50": 0.6, "PerEventP95": 0.88 }
    }
  }
}
```

`progressionLockWaits.ObservedWaiterSeconds` is the approximation `sum(waiter_count_per_sample) * sample_interval_seconds` — zero means no rebuild ever found a contended waiter on the progression row at sample time; a non-zero number signals the new concurrent-rebuild cap (jasperfx#420 in 2.9.0) is being exercised or that something outside the daemon is racing the same row.

Without `--instrument` the samplers / counter / activity are not constructed at all -- a measured rebuild has no perceptible overhead and the JSON section reads `"enabled": false` with zeroed totals.

## The `daemonload` scenario (jasperfx#486 WS2)

Everything above measures **rebuilds**; `daemonload` measures the **steady-state running
daemon** — the deployment shape whose connection footprint at ~100 tenants is the WS2
concern. It builds an isolated `UseTenantPartitionedEvents` store in the
`scaletest_daemonload` schema with `--projections` async projections, registers
`--tenants` tenants, starts the daemon (which fans out one subscription agent per
projection × tenant), appends continuously across every tenant for `--duration-seconds`,
and samples `pg_stat_activity` throughout. The store's connection string carries a
dedicated `Application Name` so the sampler counts exactly the store's connections.

The run fails (exit 1) on any append failure or any tenant whose agents don't catch up to
that tenant's own per-tenant sequence ceiling within `--catch-up-timeout-seconds`.
`--max-connections N` additionally turns the peak-connection reading into a pass/fail
gate: the WS2 goal is connections O(databases), not O(tenant agents), so run this before
and after the daemon command-batching work to quantify the win and then pin it.

### Sharded variant (marten#4882, epic jasperfx#486 WS6)

`--databases N` (N > 1) pools the tenants across N shard databases on the same server via
`MultiTenantedWithShardedDatabases` — `scaletest_dl_shard_0..N-1` are created on demand
(`--wipe` drops them first), tenants are assigned round-robin with explicit placement, the
harness runs **one projection daemon per shard**, and the `pg_stat_activity` sampler
groups the store's Application-Name-attributed connections by `datname` so every shard
database reports its own peak/mean series (the master/registry database is reported
separately). Three health assertions on top of the single-DB ones:

* **Per-database gate** — `--max-connections` is enforced per shard database; the WS6
  expectation from the 2.22.0 governors (4 event loads + 4 batch writes + HWM per
  database) is that each shard's peak mirrors the single-DB daemonload result, giving
  O(databases) total rather than O(agents)
* **Per-tenant catch-up on every shard** — every tenant's per-tenant progression rows
  reach that tenant's own sequence ceiling in its own shard database
* **Database-affine placement** — each tenant's per-tenant event sequence exists in
  exactly its assigned shard; a sequence on a foreign shard (or missing at home) fails
  the run as cross-shard bleed

```bash
# 100 tenants pooled over 4 shard databases, per-database ceiling of 16
dotnet run --project src/Marten.ScaleTesting -- daemonload \
    --databases 4 --tenants 100 --projections 2 --duration-seconds 120 --wipe \
    --max-connections 16 --metrics daemonload-sharded.json --trace daemonload-sharded.csv
```

### Multi-node native HotCold (marten#4883, epic jasperfx#486 WS6)

`daemonload-multinode` is the COORDINATOR of a multi-process scenario: multi-node here means
multiple OS processes of this same binary (no cluster infra). It provisions one
tenant-partitioned store, launches `--nodes` child processes (`daemonload-node`, an internal
worker role) each running `AddAsyncDaemon(DaemonMode.HotCold)` against the shared store on one
`DaemonLockId`, appends continuously from the coordinator, samples `pg_stat_activity` grouped by
per-node Application Name × database, optionally kills the current leader
(`--kill-leader-after-seconds`) to exercise leadership failover, and verifies per-tenant catch-up.

Under native HotCold + one tenant-partitioned database, the `MultiTenantedProjectionDistributor`
locks the database's whole per-tenant agent set to ONE leader node at a time (jasperfx#491
per-tenant fan-out runs on the winner). So the scenario asserts: single-leader **exclusivity**,
**failover** to a survivor when the leader is killed (identified as the node holding the agent
connections), **per-tenant catch-up** to every tenant's own ceiling afterward, and a per-node
connection footprint that stays governed (`--max-connections-per-node`).

```bash
# 3 nodes, kill the leader 30s in, verify a survivor takes over and every tenant catches up
dotnet run --project src/Marten.ScaleTesting -- daemonload-multinode \
    --nodes 3 --tenants 100 --projections 2 --duration-seconds 120 \
    --kill-leader-after-seconds 30 --wipe \
    --max-connections-per-node 16 --metrics daemonload-multinode.json --trace daemonload-multinode.csv
```

To get agents running on DIFFERENT nodes simultaneously (rather than one hot leader), use the
sharded topology below — multiple shard databases, whose per-database locks land on different
nodes.

### Sharded multi-node — cross-node distribution (marten#4883, epic jasperfx#486 WS6)

`daemonload-multinode-sharded` pools `--tenants` tenants across `--databases` shard databases via
`MultiTenantedWithShardedDatabases`, then launches `--nodes` HotCold node processes over that
sharded store. Because each shard database is its own leadership lock, the distributor spreads
shard leadership across nodes — node A leads `shard_0`, node B leads `shard_1` — which is the
cross-node agent distribution the epic's topology matrix calls for (single-database HotCold above
can only ever have one hot leader).

It asserts: **distribution** (leadership spans multiple nodes; `--require-distribution` makes it a
hard gate, off by default because a fast node can transiently grab every shard before the
leadership poll rebalances); **redistribution** on `--kill-node-after-seconds` (a killed node's
shards move to surviving nodes with no tenant stalled); **per-tenant catch-up** on every shard; and
a **per-node × per-shard** connection footprint that stays governed (`--max-connections-per-node-shard`).

```bash
# 3 nodes over 3 shard databases (100 tenants pooled across them); kill a node 30s in and
# verify its shards redistribute and every tenant catches up on its home shard.
dotnet run --project src/Marten.ScaleTesting -- daemonload-multinode-sharded \
    --nodes 3 --databases 3 --tenants 100 --projections 2 --duration-seconds 120 \
    --kill-node-after-seconds 30 --wipe \
    --max-connections-per-node-shard 16 --metrics daemonload-multinode-sharded.json \
    --trace daemonload-multinode-sharded.csv
```

The remaining WS6 multi-node work is the Wolverine-managed distribution mode (automatic per-tenant
fan-out + affinity from wolverine#3328); this harness references only Marten, not Wolverine, so
that variant lives in Wolverine's own load suite.

## The `rebuildload` scenario (marten#4884, epic jasperfx#486 WS6/WS3)

Where `daemonload` measures the running daemon, `rebuildload` measures **concurrent projection
rebuilds** and sweeps the three governor knobs so their shipped defaults can be confirmed — or a
change recommended — from evidence, without ever mutating a shipped default (measurement tool
only). It registers `--projections N` independent async projections over `--tenants`
per-tenant-partitioned tenants, seeds `--events-per-tenant` events (idempotent by row count),
then for each configuration in the sweep:

* applies the two inner governors (`MaxConcurrentEventLoadsPerDatabase`,
  `MaxConcurrentBatchWritesPerDatabase`) and optionally `EnableExtendedProgressionTracking`,
* rebuilds every projection with the outer fan-out throttled to the swept cap
  (`MaxConcurrentRebuildsPerDatabase`) — the same `SemaphoreSlim(cap)` shape as
  `ProjectionHost.RebuildProjectionsWithCapAsync` (jasperfx#463), whose shipped default is
  `max(1, MaxPoolSize / 8)`,
* samples `pg_stat_activity` + `mt_event_progression` lock-waits throughout,

and prints a per-configuration comparison (wall-clock, throughput, peak/mean/idle connections,
progression waiters) plus an advisory recommendation. The default cap for the current pool is
flagged with `*`.

```bash
# Sweep caps 2/4/8/12 (incl. the shipped default) and the extended-progression cost
dotnet run --project src/Marten.ScaleTesting -- rebuildload \
    --tenants 100 --projections 8 --events-per-tenant 5000 \
    --caps 2,4,8,12 --sweep-extended-progression --wipe --metrics rebuildload.json
```

### Local-scale finding (20 tenants × 8 projections × 1200 events, MaxPoolSize=100 ⇒ default cap 12)

| cap | ext | elapsed | peak conns | max progression waiters |
|----:|-----|--------:|-----------:|------------------------:|
| 2   | off | 6.0s | 3 | 0 |
| 4   | off | 5.6s | 5 | 0 |
| 8   | off | 5.3s | 9 | 0 |
| 12* | off | 5.2s | 9 | 0 |
| 12* | on  | 5.0s | 9 | 0 |

Two-layer model validated at this scale: **peak connections ≈ min(cap, projections) + 1** — the
outer cap is the dominant connection driver, and it never exceeds pool headroom (9 ≪ 100).
Wall-clock improvement flattens past cap=4 (diminishing returns). **Zero `mt_event_progression`
waiters** at every cap — the concurrent-rebuild cap creates no progression-row contention at this
scale. `EnableExtendedProgressionTracking` showed no measurable overhead (within noise).
**Recommendation: CONFIRM the shipped `max(1, MaxPoolSize/8)` default cap and the load/write
governor default of 4.** Local-scale evidence is indicative only — re-run at production pool +
event volume before amending `rebuilding.md`.

## Non-goals

* Not a microbenchmark — `src/MartenBenchmarks/` covers per-method timings.
* Not a NuGet package — internal tool only.
* Not wired into CI.
* Not distributed across machines — the sharded `daemonload` variant shards across
  databases on ONE Postgres server.
