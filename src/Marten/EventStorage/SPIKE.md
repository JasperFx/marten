# Spike: W4 — closed-shape event-storage hierarchy

**Tracking:** [#4404](https://github.com/JasperFx/marten/issues/4404) W4. Supersedes the #4400 spike framing (closed-shape generics + source-gen instead of runtime composite assembly).
**Branch:** `spike/W4-event-storage-hierarchy` (local, off `master`).
**Status:** sketch only. Nothing wired into the runtime. Discuss the seams before extending.

## What W4 replaces

Today's `EventDocumentStorageGenerator` emits a single
`GeneratedEventDocumentStorage` per-`EventGraph` at runtime via
`JasperFx.RuntimeCompiler`. Inside that one class, the append flow
branches on `AppendMode` (Full → row insert path; QuickWithVersion →
batched function call path; Quick → batched function call path with
server-timestamp variants) and emits a different `ConfigureCommand`
body per branch.

W4 splits the one runtime-emitted storage class into **three
hand-written closed-shape implementations**, one per append-mode flavor,
each with its own slim per-mode descriptor and its own concrete
operation classes:

| Append mode | Storage class | Descriptor | Operation (source-gen sample) |
|---|---|---|---|
| Full | `Rich.RichEventStorage<TId>` | `RichEventStorageDescriptor` | `Rich.RichAppendEventOperation` |
| Quick (batch) | `Quick.QuickEventStorage<TId>` | `QuickEventStorageDescriptor` | `Quick.QuickAppendEventsOperation` |
| QuickWithServerTimestamps | `QuickWithServerTimestamps.QuickWithServerTimestampsEventStorage<TId>` | `QuickWithServerTimestampsEventStorageDescriptor` | `QuickWithServerTimestamps.QuickAppendEventsWithServerTimestampsOperation` |

At `DocumentStore` construction time exactly one of the three storage
classes is instantiated based on `StoreOptions.Events.AppendMode`. The
session calls through `EventStorage<TId>`'s virtual `AppendStreamEvents`
method; the runtime never branches on append mode again after startup.

## Why three implementations instead of one

Rich and Quick diverge along three axes that all bite the per-call hot
path:

| Axis | Rich (Full mode) | Quick (batch modes) |
|---|---|---|
| SQL shape | `insert into mt_events (...) values (...)` per row | `select mt_quick_append_events(...)` with array params per column |
| Operation count per stream | N (one per event) | 1 (one batched call) |
| Parameter shape | scalars | `NpgsqlDbType.Array \| Varchar/Jsonb/Bigint/...` per column |
| RETURNING / read-back | none — event.Sequence stays default | `long[]` array; walk events backwards assigning Version + Sequence |

Trying to unify those at the operation level — one `EventStorage` with
runtime branches on mode, or a binder seam that covers both — pushes
either per-call branches into the hot path or a binder interface with
two near-disjoint method sets (per-event Bind/OnRead for Rich, per-batch
ArrayBind/BatchPostprocess for Quick). The W4 framing picks the third
option: separate the implementations at the storage class level and let
each be internally consistent.

## How configuration axes split between Rich and Quick

Each mode handles the metadata-column configuration axes (headers /
causation / correlation / username / tags) differently — a consequence
of the fundamentally different SQL shapes.

### Rich: descriptor binder array (the hybrid)

Rich's per-event `INSERT` writes scalar parameters, one per column. The
configuration axes turn into runtime-active binders that produce one
scalar each. `RichEventStorageDescriptor.MetadataBinders` is an ordered
array of `IEventMetadataBinder` (see `IEventMetadataBinder.cs`,
`Metadata/HeadersColumnBinder.cs`, `Metadata/SequenceColumnBinder.cs`).
Source-gen emits one `pb.AppendParameter(...)` statement per core column
plus a single loop over the binder array — see
`Rich/RichAppendEventOperation.cs`. Configuration toggles affect what
goes in the binder array at startup, not what the source-gen output
looks like.

This is the hybrid the previous round of the spike introduced. It lives
on the Rich side because Rich's per-event scalar binding is the case
where it pays off — every binder is a candidate for `OnRead` write-back
(sequence write-back is the canonical example).

### Quick: hand-written array binds in source-gen output

Quick's batched function call binds metadata as `NpgsqlDbType.Array | ...`
parameters — one array per column, with the array's contents being one
value per event in the stream. That shape is uniform enough across
columns that there's no per-event dispatch to abstract: the source-gen
output inlines `for (var i = 0; i < count; i++) headers[i] = ...; pb.AppendParameter(headers)`
per active column directly, no binder array. See
`Quick/QuickAppendEventsOperation.cs`.

The configuration axes DO show up in Quick's source-gen matrix (an
EventGraph with headers enabled gets a different concrete
`QuickAppendEventsOperation` than one without) but the matrix is bounded
and uniform — the generator just emits a few extra array-construction
lines per active column.

Why not the binder hybrid for Quick too: a binder interface for Quick
would need a different signature (per-batch, takes the events list,
allocates a per-binder array). The shape is uniform enough that a binder
abstraction would mostly add a virtual call per column without removing
any per-call branching from the source-gen output. The cost isn't
worth the consistency.

### QuickWithServerTimestamps: a sibling, not a flag

Today's codegen path treats `EventAppendMode.QuickWithServerTimestamps`
as a branch inside the Quick base — it emits a conditional `writeTimestamps`
helper call when the flag is set. The W4 split makes it a **separate
closed-shape storage class** (`QuickWithServerTimestamps.QuickWithServerTimestampsEventStorage`).
The choice between Quick and QuickWithServerTimestamps is the choice of
which storage class gets instantiated at startup; no per-call branches.

The actual divergence from Quick is small: one extra array parameter
(server-side `now()` timestamps) and one extra read-back in Postprocess
to assign the returned timestamps onto each event. Code-wise it's a
near-clone of `QuickAppendEventsOperation` — but the W4 framing is that
"near-clone with a slight divergence at the per-call hot path" is exactly
the case where a separate concrete subclass beats a conditional branch.

## Files

```
src/Marten/EventStorage/
├── SPIKE.md                              ← this file
├── EventStorage.cs                       ← abstract base, virtual AppendStreamEvents
├── IEventStoreSqlDialect.cs              ← Postgres / SQL Server seam
├── IEventMetadataBinder.cs               ← Rich-mode binder interface (per-event Bind + OnRead)
├── Dialects/
│   └── PostgresEventStoreDialect.cs     ← mt_events / mt_streams; spike-stubbed for most templates
├── Rich/
│   ├── RichEventStorage.cs              ← per-event INSERT path
│   ├── RichEventStorageDescriptor.cs    ← Rich SQL + binder array
│   └── RichAppendEventOperation.cs      ← source-gen sample (inlined core writes + binder loop)
├── Quick/
│   ├── QuickEventStorage.cs             ← batched function-call path
│   ├── QuickEventStorageDescriptor.cs   ← Quick SQL (no binder array)
│   └── QuickAppendEventsOperation.cs    ← source-gen sample (hand-written array binds)
├── QuickWithServerTimestamps/
│   ├── QuickWithServerTimestampsEventStorage.cs
│   ├── QuickWithServerTimestampsEventStorageDescriptor.cs
│   └── QuickAppendEventsWithServerTimestampsOperation.cs   ← source-gen sample
└── Metadata/
    ├── HeadersColumnBinder.cs            ← Rich-mode binder (write-only, opt-in)
    └── SequenceColumnBinder.cs           ← Rich-mode binder (server-set via nextval, read-back to event.Sequence)
```

## Hot-path budgets

| Path | Per-call cost |
|---|---|
| Rich AppendStreamEvents (N events) | N × { 1 virtual `ConfigureCommand`, 2 `Append`, 7 inlined `AppendParameter` core, 1–4 virtual `Bind` per binder } |
| Quick AppendStreamEvents (N events) | 1 × { 1 virtual `ConfigureCommand`, 1 `Append`, 1 scalar + ~5 array `AppendParameter` + 1 read-back loop in Postprocess walking N events } |
| QuickWithServerTimestamps | same as Quick + 1 extra array parameter + 1 extra read-back result-set walk |

No runtime branching on append mode in any of those. The mode choice
happens once at startup when the `EventGraph`'s `AppendMode` setting
picks which `EventStorage<TId>` subclass to instantiate.

## What's NOT in this spike

- `EventStorageDescriptorBuilder` — the previous round's combined-mode
  builder is gone (it built one descriptor with SQL for all modes). The
  three per-mode descriptors get their own builders that take the
  dialect + the `EventGraph` + the serializer and build only the SQL
  the mode actually uses. Mechanical; not done here.
- The dialect's SQL templates for the Quick paths
  (`QuickAppendEvents`, `QuickAppendEventsWithServerTimestamps`) are
  TODO-stubbed in `Dialects/PostgresEventStoreDialect.cs`. They'd port
  from the existing `EventDocumentStorageGenerator.buildQuickAppendOperation`
  emit site.
- `InsertStream` / `UpdateStreamVersion` / `StreamStateQueryHandler`
  on each storage class throw `NotImplementedException` — same SQL
  shape in all three modes, would be implemented identically (or
  factored to a shared base / helper). Not done here.
- Source-gen authoring (W5). The samples in `Rich/`, `Quick/`,
  `QuickWithServerTimestamps/` are what the generator would emit; the
  generator itself is a separate work stream.
- The string-stream-identity variants of each operation. The Guid
  samples are the template; string is mechanical (swap `Stream.Id` →
  `Stream.Key`, `NpgsqlDbType.Uuid` → `NpgsqlDbType.Varchar`).
- Polecat-specific dialect impl. The `IEventStoreSqlDialect` seam is
  there; the SQL Server impl ships in Polecat under JasperFx.Storage
  (W2).

## Open questions

1. **Descriptor as a class vs as static readonly fields on the closed-
   shape subclass.** Threading the descriptor as a constructor parameter
   pays one ref-typed field + one extra indirection per call. An
   alternative is for source-gen to emit SQL strings as `static readonly`
   on the concrete class — zero indirection, but ties each concrete
   class to one `EventGraph`. Worth measuring against the
   `MartenBenchmarks.V9` perf suite (#4406) once it lands.

2. **`EventStorage<TId>` base shape — abstract class vs interface.** The
   spike picks an abstract class with `NotImplementedException` stubs
   for the unbuilt operations. An interface with default-method-based
   common implementations is the alternative. Probably doesn't matter
   for v9; pick whichever reads cleaner once the operations are real.

3. **Shared base for the two Quick descriptors.** `QuickEventStorageDescriptor`
   and `QuickWithServerTimestampsEventStorageDescriptor` have a
   substantially overlapping shape. Factoring a `QuickEventStorageDescriptorBase`
   would deduplicate; the spike keeps them as flat types for symmetry
   with the three-implementations framing. Discuss once we've seen the
   matrix in full (after the optional metadata axes are wired into the
   Quick source-gen output).

4. **Metadata binder ordering contract.** `RichEventStorageDescriptor.MetadataBinders`
   and `IEventStoreSqlDialect.AppendEventFullPrefix` must agree on the
   metadata column order. Today's dialect ignores the binder list and
   only emits core-column SQL. Mechanically simple to fix; flagged for
   the next spike iteration.

5. **Per-call allocation.** Rich allocates N operation instances per
   stream append (one per event); Quick allocates 1. W4 keeps both —
   pooling is a separate optimization, the same one the #4400 spike
   flagged. Probably v10.

6. **`StreamStateQueryHandler` placement.** It's currently abstract on
   `EventStorage<TId>` for symmetry with the existing
   `EventDocumentStorage`. Arguably it belongs in a separate query-handler
   hierarchy that mirrors but isn't part of storage. Keep where it is or
   factor out?

7. **Polecat dialect parity.** `IEventStoreSqlDialect` is sketched with
   coarse-grained SQL templates. Polecat may prefer fragment-builder
   primitives to compose its SQL Server output. Need to coordinate with
   the Polecat maintainer per #4404 open question (5) before W2 cuts
   the JasperFx.Storage repo.

## Discussion checklist

- Three IEventStorage impls vs one: confirm the split.
- Rich-mode binder hybrid + Quick-mode hand-written-arrays asymmetry: is
  that the right tradeoff, or do we want a Quick-mode binder seam too
  for consistency?
- Descriptor-as-class vs source-gen-emitted-static-readonly fields: pick
  one or measure both?
- `IEventStoreSqlDialect` granularity: SQL templates (today's sketch) vs
  fragment-builder primitives. Polecat-coordination call before W2?
- Sequencing: W4 → W1+W2 → W5 (source-gen), or land W1+W2 first to give
  W4 a JasperFx.Storage home?
