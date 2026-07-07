# `Marten.EventStorage` — closed-shape event-storage hierarchy

Hand-written, per-`AppendMode` event-storage implementations that replace
the runtime-Roslyn-emitted `GeneratedEventDocumentStorage` for the
event-store write path. Opt-in via `StoreOptions.Events.UseClosedShapeStorage = true`
in Marten 9; planned default-on in 10.

See the [`UseClosedShapeStorage` migration-guide section](../../../docs/migration-guide.md#closed-shape-event-storage)
for the consumer-facing intro and the v9 / v10 / v11 transition plan.

> **#4821 event-storage extraction (in progress).** The dialect-neutral pieces of this
> hierarchy are being moved into the shared `Weasel.Storage` package so Marten and Polecat
> (SQL Server) share them. As of Weasel 9.15.0 (event E2) the descriptor classes
> (`RichEventStorageDescriptor` / `QuickEventStorageDescriptor` /
> `QuickWithServerTimestampsEventStorageDescriptor`), `IEventMetadataBinder` + the metadata
> binders, and `IEventStoreSqlDialect` now live in `namespace Weasel.Storage` — the in-repo
> relative links below to those specific types point at their former Marten location.
> `PostgresEventStoreDialect` (the Postgres implementation), the storage classes, and the
> operations remain here.

## Architecture overview

Three concrete `EventStorage<TId>` subclasses, one per
[`EventAppendMode`](https://github.com/JasperFx/jasperfx/blob/master/src/JasperFx.Events/EventAppendMode.cs):

| Append mode | Storage class | Descriptor |
| --- | --- | --- |
| `Rich` | `Rich/RichEventStorage<TId>` | `Rich/RichEventStorageDescriptor` |
| `Quick` | `Quick/QuickEventStorage<TId>` | `Quick/QuickEventStorageDescriptor` |
| `QuickWithServerTimestamps` | `QuickWithServerTimestamps/QuickWithServerTimestampsEventStorage<TId>` | `QuickWithServerTimestamps/QuickWithServerTimestampsEventStorageDescriptor` |

Exactly one of the three is instantiated at `DocumentStore` construction
time by [`EventStorageBuilder.Build<TId>`](EventStorageBuilder.cs) based
on `EventGraph.AppendMode`. The runtime never branches on append mode
again after that: per-session dispatch is a virtual call through
[`EventStorage<TId>`](EventStorage.cs).

[`ClosedShapeEventDocumentStorage`](ClosedShapeEventDocumentStorage.cs)
is the adapter that bridges this hierarchy to Marten's existing
`EventDocumentStorage` contract — it owns the read-path `ISelector<IEvent>`
(which still uses the codegen-emitted `ApplyReaderDataToEvent` in 9.0)
and delegates every write-path method to the `EventStorage<TId>`
instance the builder produced.

### Why three implementations instead of one

Rich and Quick diverge along three axes that all bite the per-call hot
path:

| Axis | Rich (Full mode) | Quick (batch modes) |
| --- | --- | --- |
| SQL shape | `insert into mt_events (...) values (...)` per row | `select mt_quick_append_events(...)` with array params per column |
| Operation count per stream | N (one per event) | 1 (one batched call) |
| Parameter shape | scalars | `NpgsqlDbType.Array \| Varchar/Jsonb/Bigint/...` per column |
| RETURNING / read-back | none — `event.Sequence` stays default | `long[]` array; walk events backwards assigning `Version` + `Sequence` |

Unifying those at the operation level pushes either per-call branches
into the hot path or a binder interface with two near-disjoint method
sets (per-event `Bind`/`OnRead` for Rich, per-batch `ArrayBind`/`BatchPostprocess`
for Quick). Splitting at the storage-class level keeps each
implementation internally consistent.

`QuickWithServerTimestamps` is a sibling, not a flag — the extra
server-side `now()` timestamp array + return-set walk diverges enough
from plain Quick that a separate concrete class beats a conditional
branch.

## How the configuration axes map onto each mode

The metadata-column axes (`headers`, `causation_id`, `correlation_id`,
`user_name`, the per-event sequence) toggle on/off per `EventGraph`
configuration. Rich and Quick handle that differently:

### Rich: `IEventMetadataBinder` array (the hybrid)

Rich's per-event `INSERT` writes scalar parameters, one per column.
[`RichEventStorageDescriptor.MetadataBinders`](Rich/RichEventStorageDescriptor.cs)
is an ordered array of [`IEventMetadataBinder`](IEventMetadataBinder.cs).
[`Rich/RichAppendEventOperation`](Rich/RichAppendEventOperation.cs) writes
the inlined core columns then loops over the binder array — one virtual
`Bind` call per active metadata column, in lockstep with the SQL prefix's
column order.

Adding a new Rich-mode metadata column:

1. Add an `IEventMetadataBinder` in [`Metadata/`](Metadata/) — see
   [`Metadata/HeadersColumnBinder.cs`](Metadata/HeadersColumnBinder.cs)
   for the simple write-only shape or
   [`Metadata/SequenceColumnBinder.cs`](Metadata/SequenceColumnBinder.cs)
   for the server-set-with-write-back shape.
2. Add a switch arm in `SelectRichMetadataBinders` and (if the column
   participates in the QuickWithVersion path) `SelectQuickModeMetadataBinders`
   in [`Dialects/PostgresEventStoreDialect.cs`](Dialects/PostgresEventStoreDialect.cs).
3. The dialect's `BuildAppendEventFullColumnsAndPrefix` picks up the new
   column from `EventsTable.SelectColumns()` automatically — the dialect
   builds SQL and the binder array in lockstep, so a mismatch shows up
   as a parameter-count vs column-count discrepancy at the very first
   append.

### Quick: hand-written array binds

Quick's batched function call binds metadata as
`NpgsqlDbType.Array | ...` parameters — one array per column, filled
in `QuickAppendEventsOperation.ConfigureCommand` directly. No binder
array; the per-column write code is inlined per-mode-flag in the dialect's
`BuildQuickAppendEventsSql` + the operation's `ConfigureCommand` body.
Adding a new Quick-mode optional column means extending both that SQL
builder and the operation's `ConfigureCommand`.

The asymmetry is intentional — see the per-axis tradeoff table above.

### Per-event `QuickWithVersion` INSERT path

Used by both Quick / QuickWithServerTimestamps (for new streams + streams
with `ExpectedVersionOnServer.HasValue`) and Rich (for the side-effect
event replay path called by JasperFx.Events `EventSlice.BuildOperations`,
[#4428](https://github.com/JasperFx/marten/pull/4434)). The operation
class
[`Quick/QuickAppendEventWithVersionOperation`](Quick/QuickAppendEventWithVersionOperation.cs)
is shared cross-namespace; the per-mode descriptor supplies a slightly
different SQL suffix (`", nextval('schema.mt_events_sequence'))"` for
server-claimed sequence, `")"` for bound sequence) and a different
binder array (with vs without `SequenceColumnBinder`).

## Seams

### `IEventStoreSqlDialect`

[`IEventStoreSqlDialect`](IEventStoreSqlDialect.cs) — `internal` — has
one method per append mode that returns a fully-built descriptor:

```csharp
RichEventStorageDescriptor BuildRichDescriptor(EventGraph, ISerializer);
QuickEventStorageDescriptor BuildQuickDescriptor(EventGraph, ISerializer);
QuickWithServerTimestampsEventStorageDescriptor BuildQuickWithServerTimestampsDescriptor(EventGraph, ISerializer);
```

The dialect owns SQL strings, metadata-column ordering, and binder
selection as one joint concern (not three independent ones) — that's
how the SQL stays aligned with the parameter binds.

Marten ships [`Dialects/PostgresEventStoreDialect`](Dialects/PostgresEventStoreDialect.cs).
The SQL Server dialect (Polecat) lands after `JasperFx.Storage` (W2)
cuts.

### `IEventMetadataBinder`

[`IEventMetadataBinder`](IEventMetadataBinder.cs) — the Rich-mode
per-column abstraction. One `Bind(IGroupedParameterBuilder, StreamAction, IEvent, IMartenSession)`
method (write-side) plus an optional `OnRead` for server-set columns.

Implementations live in [`Metadata/`](Metadata/):

* `SequenceColumnBinder` — server-set via `nextval()`, writes back to
  `event.Sequence` from the prepared-statement parameter.
* `HeadersColumnBinder`, `CausationIdColumnBinder`,
  `CorrelationIdColumnBinder`, `UserNameColumnBinder` — write-only,
  opt-in based on `EventGraph.MetadataConfig`.

## What still uses codegen

The flag covers the entire **write** path. The **read** path
(`ApplyReaderDataToEvent`, `ApplyReaderDataToEventAsync`) is still
codegen-emitted in 9.0 — closing that out is a follow-up tracked on the
Marten.SourceGenerator stream. `ClosedShapeEventDocumentStorage`
inherits the read-side `ISelector<IEvent>` from
`EventDocumentStorage`, which keeps using the existing per-event-type
codegen.

## Cross-references

* Parent epic — [#4410](https://github.com/JasperFx/marten/issues/4410) (closed by PR [#4431](https://github.com/JasperFx/marten/pull/4431)).
* Source-gen compiled queries (the analogous LINQ-side work) — [#4405](https://github.com/JasperFx/marten/issues/4405).
* Open follow-ups for v10:
  * Read-side closed-shape (`ApplyReaderDataToEvent`) — Marten.SourceGenerator.
  * Polecat SQL Server dialect implementation — pending `JasperFx.Storage` cut.
