# TimescaleDB Support

TimescaleDB support lets Marten turn its tables into
[TimescaleDB](https://github.com/timescale/timescaledb) **hypertables** — automatically time-partitioned
tables with columnar compression, retention policies, and continuous aggregates. It ships **in the core
Marten package** under the MIT license, scoped behind its own `Marten.TimescaleDB` namespace, and is
entirely opt-in at runtime via `UseTimescaleDB()` — stores that never call it pay nothing.

What it gives you today:

- a one-line `UseTimescaleDB()` opt-in that registers the `timescaledb` extension on every database Marten manages
- `ProjectionAsHypertable<T>()` to turn a time-bucketed [flat table projection](/events/projections/flat) into a hypertable, with configurable chunk interval, compression, retention, and continuous aggregates
- `DocumentAsHypertable<T>()` to turn an append-heavy document table (audit logs, metrics, activity records) into a hypertable partitioned by one of its own timestamp members
- full participation in Marten's schema migration model — the hypertable, its policies, and its continuous aggregates are created idempotently through the normal `ApplyAllConfiguredChangesToDatabaseAsync` path, and do **not** show up as drift on subsequent migrations

## Requirements

The feature ships in core Marten, so there is no separate package to install — reach it with
`using Marten.TimescaleDB;` and enable it with `UseTimescaleDB()`.

TimescaleDB is a **loadable module**: unlike PostGIS or pgvector it must be listed in
`shared_preload_libraries` before `CREATE EXTENSION timescaledb` will succeed. The official
[`timescale/timescaledb`](https://hub.docker.com/r/timescale/timescaledb) images already do this. This repo
ships `docker-compose.timescaledb.yml` (which runs on port 5433 so it can coexist with the main dev
database) for local development, and a dedicated CI workflow using the `timescale/timescaledb-ha` image.

## Enabling TimescaleDB on a store

```csharp
using Marten;
using Marten.TimescaleDB;

var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    // Registers CREATE EXTENSION IF NOT EXISTS timescaledb on every database
    opts.UseTimescaleDB();
});
```

## Flat table projections as hypertables

The cleanest, highest-value fit is a [flat table projection](/events/projections/flat) that rolls up
events into a **time-bucketed** table — per-minute/per-hour metrics, IoT rollups, activity counters, and
the like. Because the projection's table is written by the async daemon (or inline), TimescaleDB then gives
you time-chunked storage, columnar compression of old chunks, continuous aggregates for dashboards, and
automatic retention — all declaratively.

```csharp
opts.Projections.Add(new MetricsProjection(), ProjectionLifecycle.Async);

opts.UseTimescaleDB(ts =>
{
    ts.ProjectionAsHypertable<MetricsProjection>("captured_at", hyper =>
    {
        hyper.ChunkInterval = TimeSpan.FromHours(1);
        hyper.CompressAfter = TimeSpan.FromDays(30);
        hyper.RetainFor     = TimeSpan.FromDays(365);
        hyper.ContinuousAggregate("hourly_metrics", "1 hour",
            "avg(value) as avg_val, max(value) as max_val");
    });
});
```

```csharp
public class MetricsProjection: FlatTableProjection
{
    public MetricsProjection(): base("sensor_metrics", SchemaNameSource.EventSchema)
    {
        // The single primary key IS the time column — see the constraint below.
        Table.AddColumn<DateTimeOffset>("captured_at").AsPrimaryKey();
        Table.AddColumn<double>("value").NotNull();

        Project<SensorReadingRecorded>(map =>
        {
            map.Map(x => x.Value, "value");
        }, tablePrimaryKeySource: x => x.CapturedAt);
    }
}
```

### Configuration options

| Property | Maps to | Notes |
| --- | --- | --- |
| `ChunkInterval` | `create_hypertable(..., chunk_time_interval => ...)` | Width of each time chunk. Defaults to TimescaleDB's own default (7 days). |
| `CompressAfter` | `ALTER TABLE ... SET (timescaledb.compress ...)` + `add_compression_policy(...)` | Enables columnar compression of chunks older than this age. |
| `CompressSegmentBy` / `CompressOrderBy` | compression settings | Optional segment-by / order-by keys. Order-by defaults to the time column `DESC`. |
| `RetainFor` | `add_retention_policy(...)` | Drops chunks older than this age. |
| `ContinuousAggregate(view, bucket, select, groupBy?)` | `CREATE MATERIALIZED VIEW ... WITH (timescaledb.continuous)` | A self-refreshing rollup view. Marten creates the view `WITH NO DATA`; set the refresh policy (`add_continuous_aggregate_policy`) with your own operational tooling. |

::: warning Compression / retention policies are applied on creation only
The compression and retention settings (`CompressAfter`, `RetainFor`, `CompressSegmentBy`/`CompressOrderBy`)
are emitted when the hypertable is first created. Later **changes** to those values are not diffed and
re-applied on subsequent migrations — adjust an existing policy with TimescaleDB's own
`add_/remove_compression_policy` / `add_/remove_retention_policy` functions (or drop and recreate the
hypertable). The hypertable, its policies, and its continuous aggregates are otherwise created idempotently
and do not show up as drift.
:::

::: warning The partition column must be the projection's primary key
TimescaleDB requires the partitioning column to participate in **every** unique/primary key on a hypertable.
A `FlatTableProjection` always has exactly one primary-key column and upserts `ON CONFLICT` against it, so the
only shape that maps cleanly onto a hypertable is one where that **single primary-key column is the time
column** (a time-bucketed rollup). If you configure `ProjectionAsHypertable` against a projection whose
primary key is something else (e.g. the stream id), Marten fails fast at schema-application time with a
descriptive error rather than letting TimescaleDB reject the `create_hypertable` call.
:::

## Document tables as hypertables

Append-heavy document types — audit logs, metrics, activity records — can be stored in a hypertable
partitioned by one of their own timestamp members:

```csharp
opts.UseTimescaleDB(ts =>
{
    ts.DocumentAsHypertable<AuditEntry>(x => x.CreatedAt, hyper =>
    {
        hyper.ChunkInterval = TimeSpan.FromDays(1);
        hyper.CompressAfter = TimeSpan.FromDays(30);
        hyper.RetainFor     = TimeSpan.FromDays(365);
    });
});
```

Because TimescaleDB requires the partition column to be part of the primary key, `DocumentAsHypertable`
**duplicates the selected member into a `NOT NULL` column and adds it to the document table's primary key**,
making it `(id, created_at)`. Marten's own schema model is updated to match, so there is no schema drift, and
the generated upsert / update / delete SQL picks the composite key up automatically (the same machinery that
backs list- and range-partitioned document tables).

::: warning The partition member must be immutable
Because the timestamp is now part of the primary key, it must **not change** for a given document id. Marten's
update path matches on the full primary key, so mutating the timestamp after the first `Store` would fail to
find the existing row. `DocumentAsHypertable` is intended for append-heavy types whose timestamp is set once on
creation and never modified. Loading and deleting by id still work (there is exactly one row per id), though a
load by id alone cannot use chunk exclusion and will scan all chunks — query by the time column, or by id plus
a time range, for time-partitioned performance.
:::

## Multi-tenancy

Hypertables work with Marten's conjoined (single-table) tenancy — the tenant column is just another column on
the chunked table. For database-per-tenant, each tenant database needs the `timescaledb` extension;
`UseTimescaleDB()` registers it on every database Marten manages, so this is handled for you.

## The event store is intentionally out of scope

The original proposal ([#4980](https://github.com/JasperFx/marten/issues/4980)) also floated turning the
**`mt_events`** table into a hypertable. This is deliberately **not** supported: it would require adding
`timestamp` to the `seq_id` primary key **and** to the `(stream_id, version)` optimistic-concurrency unique
index — weakening that concurrency guarantee — plus reworking the tag-table foreign keys. The `mt_events` and
`mt_streams` tables are left untouched by this extension. Use `UseArchivedStreamPartitioning` or the
tenant-partitioned event options for native event-table partitioning instead.
