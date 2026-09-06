# `Marten.EventStorage` — closed-shape event-storage hierarchy

Hand-written, per-`AppendMode` event-storage implementations that replace
the runtime-Roslyn-emitted `GeneratedEventDocumentStorage` for the
event-store write path. Opt-in via `StoreOptions.Events.UseClosedShapeStorage = true`
in Marten 9; planned default-on in 10.

See the [`UseClosedShapeStorage` migration-guide section](../../../docs/migration-guide.md#closed-shape-event-storage)
for the consumer-facing intro and the v9 / v10 / v11 transition plan.

> **#4821 event-storage extraction — done.** The dialect-neutral half of this hierarchy now
> lives in the shared `Weasel.Storage` package, so Marten (Postgres), Polecat (SQL Server) and
> Fisher (SQLite) share it. **Most type names mentioned below resolve to `namespace
> Weasel.Storage`, not to a file in this directory** — see the two tables in
> [What lives where](#what-lives-where). What remains in this folder is Postgres-specific SQL
> plus the adapter onto Marten's own `EventDocumentStorage` contract; the remaining follow-ups
> are enumerated in [#5339](https://github.com/JasperFx/marten/issues/5339).

## What lives where

Everything dialect-neutral is in **`Weasel.Storage`** (`Weasel.Storage/Events/`):

| Concern | Types |
| --- | --- |
| Storage hierarchy | `EventStorage<TId>`, `Rich/RichEventStorage<TId>`, `Quick/QuickEventStorage<TId>`, `QuickWithServerTimestamps/QuickWithServerTimestampsEventStorage<TId>` |
| Construction | `EventStorageBuilder` |
| Descriptors | `RichEventStorageDescriptor`, `QuickEventStorageDescriptor`, `QuickWithServerTimestampsEventStorageDescriptor` |
| Seams | `IEventStoreSqlDialect`, `IEventMetadataBinder`, `EventAuxiliaryOperations` |
| Metadata binders | `Metadata/{Sequence,Headers,CausationId,CorrelationId,UserName}ColumnBinder` |
| Shared operations | `Operations/{AppendEventOperationBase,InsertStreamOperationBase,UpdateStreamVersionOperationBase,QuickAppendEventWithVersionOperation,AssertStreamVersionOperation}` and the per-mode insert-stream / update-stream-version operations |

What stays **here in Marten**, and why:

| File | Why it does not move |
| --- | --- |
| `Dialects/PostgresEventStoreDialect.cs` | The Postgres half of the `IEventStoreSqlDialect` seam — store-specific by construction. |
| `ClosedShapeEventDocumentStorage.cs` | Adapter from `EventStorage<TId>` onto Marten's own `EventDocumentStorage` contract. Polecat's equivalent is its `ClosedShapeOperationAdapter`. |
| `Quick/QuickAppendEventsOperation.cs`, `QuickWithServerTimestamps/QuickAppendEventsWithServerTimestampsOperation.cs`, and their base `Marten.Events.Operations.QuickAppendEventsOperationBase` | Call sites for the `mt_quick_append_events` PL/pgSQL function: one `NpgsqlDbType.Array \| …` parameter per column, a returned `long[]`. Polecat and Fisher implement `Weasel.Storage.IStorageOperation` **directly** with a fundamentally different command shape (per-event `INSERT … OUTPUT` for SQL Server), so there is no three-way base to extract. |
| `Querying/ClosedShapeStreamStateQueryHandler.cs`, `StreamStateSql.cs` | Coupled to Marten's `StreamStateQueryHandler` and its `ISelector<StreamState>`, and to the `mt_streams` column list. Lifting these needs the `StreamState` query pipeline neutralised first — a separate piece of work, not #4821. |
| `../Events/Schema/QuickAppendEventFunction.cs`, the `mt_append_event` PL/pgSQL | No analogue in the other stores. |

Three `public abstract` operation bases in `../Events/Operations/`
(`AppendEventOperationBase`, `InsertStreamBase`, `UpdateStreamVersion`) are leftovers of the
pre-#4821 codegen write path with no subclasses and no call sites. They are `[Obsolete]` for
9.x source compatibility and are deleted in v10 — do not add new code against them.

## Architecture overview

Three concrete `EventStorage<TId>` subclasses, one per
[`EventAppendMode`](https://github.com/JasperFx/jasperfx/blob/master/src/JasperFx.Events/EventAppendMode.cs):

| Append mode | Storage class (`Weasel.Storage`) | Descriptor (`Weasel.Storage`) |
| --- | --- | --- |
| `Rich` | `Rich/RichEventStorage<TId>` | `RichEventStorageDescriptor` |
| `Quick` | `Quick/QuickEventStorage<TId>` | `QuickEventStorageDescriptor` |
| `QuickWithServerTimestamps` | `QuickWithServerTimestamps/QuickWithServerTimestampsEventStorage<TId>` | `QuickWithServerTimestampsEventStorageDescriptor` |

Exactly one of the three is instantiated at `DocumentStore` construction
time by `Weasel.Storage.EventStorageBuilder.Build<TId>` based on
`EventGraph.AppendMode`, with the Postgres dialect supplied by
[`ClosedShapeEventDocumentStorage`](ClosedShapeEventDocumentStorage.cs)'s
constructor. The runtime never branches on append mode again after that:
per-session dispatch is a virtual call through `EventStorage<TId>`.

[`ClosedShapeEventDocumentStorage`](ClosedShapeEventDocumentStorage.cs)
is the adapter that bridges this hierarchy to Marten's existing
`EventDocumentStorage` contract — it owns the read path (walking
`IEventTableColumn.ReadValueSync` / `ReadValueAsync` over a column list
derived from `EventsTable.SelectColumns()`) and delegates every
write-path method to the `EventStorage<TId>` instance the builder
produced.

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
`Weasel.Storage.RichEventStorageDescriptor.MetadataBinders` is an ordered
array of `Weasel.Storage.IEventMetadataBinder`.
`Weasel.Storage.RichAppendEventOperation` writes
the inlined core columns then loops over the binder array — one virtual
`Bind` call per active metadata column, in lockstep with the SQL prefix's
column order.

Adding a new Rich-mode metadata column:

1. Add an `IEventMetadataBinder` in `Weasel.Storage/Events/Metadata/` —
   see `HeadersColumnBinder` for the simple write-only shape or
   `SequenceColumnBinder` for the server-set-with-write-back shape. (These
   are in the shared package, so a new binder is available to Polecat and
   Fisher too; only the dialect's selection of it is Marten's.)
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
class `Weasel.Storage.QuickAppendEventWithVersionOperation` is shared
cross-mode; the per-mode descriptor supplies a slightly
different SQL suffix (`", nextval('schema.mt_events_sequence'))"` for
server-claimed sequence, `")"` for bound sequence) and a different
binder array (with vs without `SequenceColumnBinder`).

## Seams

### `IEventStoreSqlDialect`

`Weasel.Storage.IEventStoreSqlDialect` — `public`, since the
implementations live in the stores — has one method per append mode that
returns a fully-built descriptor, plus an optional auxiliary-operations
factory:

```csharp
RichEventStorageDescriptor BuildRichDescriptor(EventRegistry, IStorageSerializer);
QuickEventStorageDescriptor BuildQuickDescriptor(EventRegistry, IStorageSerializer);
QuickWithServerTimestampsEventStorageDescriptor BuildQuickWithServerTimestampsDescriptor(EventRegistry, IStorageSerializer);
EventAuxiliaryOperations? BuildAuxiliaryOperations(EventRegistry) => null;
```

Configuration arrives as the neutral `EventRegistry` (Marten's
`EventGraph` derives from it, and the dialect downcasts) and
serialization through the neutral `IStorageSerializer`.

The dialect owns SQL strings, metadata-column ordering, and binder
selection as one joint concern (not three independent ones) — that's
how the SQL stays aligned with the parameter binds.

Marten ships [`Dialects/PostgresEventStoreDialect`](Dialects/PostgresEventStoreDialect.cs),
which also implements `BuildAuxiliaryOperations` (archive / tombstone /
projection-progression). Polecat ships `SqlServerEventStoreDialect` and
Fisher `SqliteEventStoreDialect` against the same seam.

### `IEventMetadataBinder`

`Weasel.Storage.IEventMetadataBinder` — the Rich-mode per-column
abstraction. One `Bind` method (write-side) plus an optional `OnRead` for
server-set columns.

Implementations live in `Weasel.Storage/Events/Metadata/`:

* `SequenceColumnBinder` — server-set via `nextval()`, writes back to
  `event.Sequence` from the prepared-statement parameter.
* `HeadersColumnBinder`, `CausationIdColumnBinder`,
  `CorrelationIdColumnBinder`, `UserNameColumnBinder` — write-only,
  opt-in based on `EventGraph.MetadataConfig`.

## What still uses codegen

Nothing on this path. The flag covers the whole **write** path, and the
**read** path was closed out too (#4411): `ApplyReaderDataToEvent` /
`ApplyReaderDataToEventAsync` walk `IEventTableColumn.ReadValueSync` /
`ReadValueAsync` over a column list derived from
`EventsTable.SelectColumns()`, so no per-event-type codegen is emitted
for event storage in either direction. (Document storage is a separate
question; this note is only about the event store.)

## Cross-references

* Parent epic — [#4410](https://github.com/JasperFx/marten/issues/4410) (closed by PR [#4431](https://github.com/JasperFx/marten/pull/4431)).
* Source-gen compiled queries (the analogous LINQ-side work) — [#4405](https://github.com/JasperFx/marten/issues/4405).
* #4821 extraction into `Weasel.Storage` and its remaining follow-up map — [#5339](https://github.com/JasperFx/marten/issues/5339).
* Open follow-ups for v10:
  * Delete the three `[Obsolete]` codegen-era operation bases in `../Events/Operations/`.
  * Flip `UseClosedShapeStorage` to default-on.
