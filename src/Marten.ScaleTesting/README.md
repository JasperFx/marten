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
| `Instrumentation/` | `--instrument`-gated rebuild observability (#4684 Phase E.1) — `RebuildInstrumentation`, `ProgressSampler`, `NpgsqlCommandCounter`. No-op when off, zero overhead. |

## Phases

* **Phase A:** project scaffold + lifted Telehealth domain + event seeder + `seed` subcommand. Idempotent via `mt_events` row count check.
* **Phase B:** 4+2+2 composite topology (stage 1: single-stream snapshots + `AppointmentMetricsProjection`; stage 2: `AppointmentDetailsProjection` + `BoardSummaryProjection`; stage 3: NEW `ProviderUtilizationProjection` + `TenantDailyRollupProjection`) + `rebuild` subcommand on the single-pass `CompositeReplayExecutor` path.
* **Phase C:** `validate` (single-shard baseline diff) + `stress` (chain `seed` + `rebuild` + `validate`) + JSON metrics sink.
* **Phase D:** use it. Drive the daemon-thread-safety fixes against the harness — each fix should hold the crash gate AND not regress rebuild time.
* **Phase E.1 (this PR):** per-period throughput sampling + Npgsql round-trip counting via `--instrument`. Surfaces p50/p95/p99/max throughput + total command count in the console summary and `--metrics` JSON; per-sample CSV trace under `--instrument-trace`. Out of scope and pinned as follow-ups (need JasperFx-side or core-Marten hooks that don't exist yet): per-batch wall-clock breakdown, per-stage composite timing, RecentlyUsedCache hit/miss, lookup-count per `EvolveAsync`, progression-lock wait time.

## Measuring a rebuild

Add `--instrument` to either subcommand to enable per-period sampling against `mt_event_progression` and Npgsql round-trip counting. Default 1s sample interval; tune with `--instrument-sample-seconds`. The console summary picks up four extra rows (p50 / p95 / max throughput, total Npgsql commands, sample count). `--metrics <path>` writes a JSON file with the rolled-up percentiles; `--instrument-trace <path>` adds per-sample CSV.

```bash
# Instrumented rebuild against existing seeded data, JSON + CSV output
dotnet run --project src/Marten.ScaleTesting -- rebuild \
    --instrument \
    --instrument-sample-seconds 1.0 \
    --instrument-trace rebuild-trace.csv \
    --metrics rebuild-metrics.json

# Chained stress run with instrumentation on the rebuild phase only
dotnet run --project src/Marten.ScaleTesting -- stress \
    --wipe --tenants 50 --events-per-tenant 400000 --writers 8 \
    --shard-timeout-seconds 3600 --baseline scaletest-baseline.json \
    --instrument --instrument-trace stress-trace.csv
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
    "throughput": { "P50": 9986, "P95": 10965, "P99": 10965, "Mean": 8972, "Max": 10965 }
  }
}
```

Without `--instrument` the sampler / counter / activity are not constructed at all -- a measured rebuild has no perceptible overhead and the JSON section reads `"enabled": false` with zeroed totals.

## Non-goals

* Not a microbenchmark — `src/MartenBenchmarks/` covers per-method timings.
* Not a NuGet package — internal tool only.
* Not wired into CI.
* Not sharded-PG or distributed.
