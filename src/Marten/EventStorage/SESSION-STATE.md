# W4 session state — for resumption after compaction

**Last updated:** during the in-flight W4 session.

This file is a working state dump so a fresh Claude session can pick up
the W4 work-stream (#4410) without paging through the full transcript.
Delete it before the draft PR opens for review — it's developer-private.

## What is W4

Closed-shape event-storage hierarchy. See `SPIKE.md` in this directory for
the full design discussion (which also stays useful through the production
build-out — design rationale doesn't go stale).

## Master plan + tracking

* Master plan: [#4404](https://github.com/JasperFx/marten/issues/4404)
* W4 tracking: [#4410](https://github.com/JasperFx/marten/issues/4410)
* Branch: `feature/W4-event-storage-hierarchy` (not yet pushed)

## Direction (confirmed in conversation)

* **Location 1a**: build in `Marten.EventStorage` namespace now; relocate
  to the eventual `JasperFx.Storage` repo (W2) later. Relocation is
  mechanical.
* **Coexistence 2a**: single feature flag
  (`StoreOptions.Events.UseClosedShapeStorage`). Default off in v9.
  Default flips on in v10, codegen path removed in v11.
* **Hand-write 3a**: full closed-shape variant matrix for v9 written by
  hand. Source-gen (W5) lands in v10 to open the matrix to consumer
  configurations.
* **Read-side O2**: hand-write a runtime equivalent of today's
  codegen-emitted `ApplyReaderDataToEvent`. Walks
  `EventsTable.SelectColumns()` and dispatches per column via methods
  added to `IEventTableColumn`. Confirmed in #4411.

## Landed so far (8 commits on the branch)

```
6dfcd8137  [#4412] W4: closed-shape StreamStateQueryHandler for Rich mode
1a7671daf  [#4411] W4: update SESSION-STATE for compaction-resume
f97840458  [#4411] W4 read-side: simplify reader-columns wiring + add round-trip test
04415a33e  [#4411] W4 read-side O2: closed-shape ApplyReaderDataToEvent
4ac9e216b  [#4410] W4 session-state for compaction-resume
a8d829b1d  [#4410] W4: dialect owns descriptor construction; Rich SQL ported
5ee72bd4e  [#4410] W4 foundation: feature flag + adapter + builder
<spike commit, originally on spike/W4-event-storage-hierarchy>
```

The build is clean (0 errors). The closed-shape **read** path is wired
end-to-end and covered by `Bug_4411_closed_shape_read_side` (two test
methods, green on net9.0 + net10.0). The **write** path is still stubbed
(RichAppendEventOperation is sample-only; InsertStream / UpdateStreamVersion /
QueryForStream throw NotImplementedException — these are #4412 / #4413 /
#4414). The closed-shape adapter can therefore be used to read from a
codegen-written events table, but cannot yet replace codegen end-to-end.

Pre-existing test failures: 4 cases of
`EventSourcingTests.archiving_events.prevent_append_*` fail on the
parent commit too (verified by checkout-and-rerun). Not from W4.

## Files

```
src/Marten/EventStorage/
├── SPIKE.md                                    ← design rationale (keep)
├── SESSION-STATE.md                            ← THIS FILE (delete before PR)
├── EventStorage.cs                             ← abstract base
├── IEventStoreSqlDialect.cs                    ← dialect seam
├── IEventMetadataBinder.cs                     ← Rich-mode binder seam
├── EventStorageBuilder.cs                      ← factory
├── ClosedShapeEventDocumentStorage.cs          ← EventDocumentStorage adapter
├── Dialects/
│   └── PostgresEventStoreDialect.cs           ← Postgres impl
├── Rich/
│   ├── RichEventStorage.cs
│   ├── RichEventStorageDescriptor.cs
│   └── RichAppendEventOperation.cs            ← sample operation
├── Quick/
│   ├── QuickEventStorage.cs
│   ├── QuickEventStorageDescriptor.cs
│   └── QuickAppendEventsOperation.cs          ← sample operation
├── QuickWithServerTimestamps/
│   ├── QuickWithServerTimestampsEventStorage.cs
│   ├── QuickWithServerTimestampsEventStorageDescriptor.cs
│   └── QuickAppendEventsWithServerTimestampsOperation.cs
└── Metadata/
    ├── HeadersColumnBinder.cs                  ← Rich-mode binder
    └── SequenceColumnBinder.cs                 ← Rich-mode binder
```

Also touched outside the new directory:

* `src/Marten/Events/EventGraph.cs` — added `UseClosedShapeStorage` flag
* `src/Marten/Events/EventGraph.GeneratesCode.cs` — wired flag check

## Where to resume

#4411 (read-side O2) is **landed**:

* `IEventTableColumn.ReadValueSync` / `ReadValueAsync` default-throw
  surfaces on the interface; concrete implementations on every column
  type that the codegen path's selector code targets (`EventTableColumn`,
  `SequenceColumn` + `VersionColumn` via inheritance, `StreamIdColumn`,
  `IsArchivedColumn`, `TenantIdColumn`, `CausationIdColumn`,
  `CorrelationIdColumn`, `UserNameColumn`).
* `ClosedShapeEventDocumentStorage.ApplyReaderDataToEvent` /
  `ApplyReaderDataToEventAsync` iterate `EventsTable.SelectColumns().Skip(3)`
  and dispatch per column. Reader columns are built directly on the
  adapter (same shape across all append-mode variants).
* `HeadersColumn` deliberately falls through to the default-throw — needs
  ISerializer threading on the IEventTableColumn surface to deserialize
  jsonb → Dictionary<string, object>. Follow-up of #4411 (tracked in
  the column's source comment).
* `Bug_4411_closed_shape_read_side` round-trip test passes on
  net9.0 + net10.0.

**In progress:** [#4412](https://github.com/JasperFx/marten/issues/4412).
QueryForStream is **done** (commit `6dfcd8137`):

* `ClosedShapeStreamStateQueryHandler<TId>` under
  `EventStorage/Querying/`. Composes the descriptor's
  `StreamStateSelectSql` with a per-call `where id = $1 [and tenant_id = $2]`.
  Row read is inherited from the base — same `ISelector<StreamState>`
  definition site as codegen.
* `RichEventStorageDescriptor.IsTenancyConjoined` (init-only) so the
  storage method doesn't need an EventGraph reference.
* `RichEventStorage<TId>.QueryForStream` wired up.
* Quick / QuickWithServerTimestamps still throw — they'll get the same
  wiring once their write paths exist (#4414 / #4415); wiring them now
  would just add unreachable code.

**Still to do in #4412** — paired with the Rich AppendEventOperation
port (#4413) because none of them are reachable end-to-end on their
own:

1. Port `EventDocumentStorageGenerator.buildInsertStream` → operation
   under `EventStorage/Rich/RichInsertStreamOperation.cs` plus dialect
   SQL template (replaces `BuildInsertStreamSql` TODO stub). Two SQL
   shapes: vanilla insert vs CTE-with-identity-enforcement. Each has a
   tenancy-conjoined variant. Per-stream parameter list driven by
   `StreamsTable.Columns.OfType<IStreamTableColumn>().Where(x => x.Writes)`
   — needs IStreamTableColumn.WriteValue ports analogous to #4411's
   IEventTableColumn.ReadValueSync. Estimate: 250–400 LOC of porting +
   per-tenancy-style integration tests.
2. Port `EventDocumentStorageGenerator.buildUpdateStreamVersion` →
   simpler — single SQL shape `update mt_streams set version = $1
   where id = $2 and version = $3 [and tenant_id = $4] returning version`.
3. Verify end-to-end by flipping `UseClosedShapeStorage = true` in a
   new test mirroring `Bug_4411_*` and asserting a full
   StartStream → SaveChanges → FetchStreamStateAsync → FetchStreamAsync
   round trip works.

**After #4412 lands** the natural sequence is #4413 → #4414 → #4415,
then the metadata-binder follow-ups (#4416 — including HeadersColumn
serializer threading) and the configuration-matrix sweep (#4417). See
the comment on #4410 for the full dependency graph.

## Open questions still alive

1. **Per-batch metadata binder seam for Quick mode** — the spike doesn't
   abstract this; the source-gen output inlines the per-column array
   binds. Whether to factor that into an `IQuickEventMetadataBinder`
   for symmetry with the Rich-mode `IEventMetadataBinder` is open.
   Flagged on #4416.
2. **Quick-mode Postprocess shape** — when QuickWithServerTimestamps
   adds the timestamp read-back, the read-back returns either an
   extended single array or multiple result-sets. The SQL function
   shape decides this; tracking on #4415.
3. **Configuration-matrix variants count** — number of closed-shape
   variant subclasses Marten needs to ship for v9. Decision: keep the
   storage classes generic on `TId` and use descriptor-based
   configuration where possible; only emit distinct subclasses where
   the per-call hot path would otherwise need a branch. Tracking on
   #4417.

## Useful commands

```bash
# Build the affected slice
dotnet build src/Marten/Marten.csproj -c Debug

# Run the event-sourcing suite (when operations are wired)
./build.sh test-event-sourcing

# Find an existing test to use as a template
grep -rln "BugIntegrationContext\|IntegrationContext" src/EventSourcingTests/Bugs | head

# Inspect the codegen path's emit sites (port from these)
src/Marten/Events/CodeGeneration/EventDocumentStorageGenerator.cs
src/Marten/Events/Schema/EventsTable.cs
src/Marten/Events/Schema/*.cs                  # column types
```
