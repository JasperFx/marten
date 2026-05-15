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

## Foundation landed (3 commits on the branch)

```
a8d829b1d  [#4410] W4: dialect owns descriptor construction; Rich SQL ported
5ee72bd4e  [#4410] W4 foundation: feature flag + adapter + builder
<spike commit, originally on spike/W4-event-storage-hierarchy>
```

The build is clean. `dotnet build src/Marten/Marten.csproj -c Debug`
produces zero errors. No tests run yet — the storage operations are
still stubbed.

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

**Next concrete unit of work:** [#4411](https://github.com/JasperFx/marten/issues/4411)
(read-side O2). Self-contained, no dependencies. Approach:

1. Add `ReadValueSync(DbDataReader, int ordinal, IEvent @event)` and
   `ReadValueAsync(DbDataReader, int ordinal, IEvent @event, CancellationToken) → Task`
   to `Marten.Events.Schema.IEventTableColumn`.
2. Implement on each concrete column type
   (`SequenceColumn`, `EventTypeColumn`, `VersionColumn`, `StreamIdColumn`,
   `EventTableColumn`, `EventJsonDataColumn`, `IsArchivedColumn`,
   `DotNetTypeColumn`, `TenantIdColumn`, and the `MetadataColumn`
   instances added via `events.Metadata.X`). Each method ports the
   body of the existing `GenerateSelectorCodeSync` / `Async` methods
   into direct C#.
3. Add `IEventTableColumn[] ReaderColumns` property to
   `RichEventStorageDescriptor` (and threadable through the Quick
   variants when their read-back paths land).
4. Override `ClosedShapeEventDocumentStorage.ApplyReaderDataToEvent`
   and `ApplyReaderDataToEventAsync` to iterate the descriptor's
   reader column list. Start at column ordinal 3 (the first three —
   `data` / `type` / `mt_dotnet_type` — are handled by the base
   `ISelector<IEvent>` already; today's codegen skips them too).
5. Add a round-trip test in `EventSourcingTests` that:
   * Constructs a store with `UseClosedShapeStorage = true`
   * Stores an event via the Rich path
   * Reads it back and verifies all metadata fields are populated

**After #4411 lands** the natural sequence is #4412 → #4413 → first
draft PR. See the comment on #4410 for the full dependency graph.

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
