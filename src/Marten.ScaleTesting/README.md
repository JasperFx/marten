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

## Phases

* **Phase A (this PR):** project scaffold + lifted Telehealth domain + event seeder + `seed` subcommand. Idempotent via `mt_events` row count check.
* **Phase B:** 4+2+2 composite topology (stage 1: single-stream snapshots + `AppointmentMetricsProjection`; stage 2: `AppointmentDetailsProjection` + `BoardSummaryProjection`; stage 3: NEW `ProviderUtilizationProjection` + `TenantDailyRollupProjection`) + `rebuild` subcommand on the single-pass `CompositeReplayExecutor` path.
* **Phase C:** `validate` (single-shard baseline diff) + `stress` (chain `seed` + `rebuild` + `validate`) + JSON metrics sink.
* **Phase D:** use it. Drive the daemon-thread-safety fixes against the harness — each fix should hold the crash gate AND not regress rebuild time.

## Non-goals

* Not a microbenchmark — `src/MartenBenchmarks/` covers per-method timings.
* Not a NuGet package — internal tool only.
* Not wired into CI.
* Not sharded-PG or distributed.
