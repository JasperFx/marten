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

## Landed so far (10 commits on the branch)

```
342806b52  [#4412 #4413] W4 Rich write-path: InsertStream + UpdateStreamVersion + AppendEvent end-to-end
62f1dcbbe  [#4412] W4: SESSION-STATE — QueryForStream done, InsertStream/UpdateStreamVersion next
6dfcd8137  [#4412] W4: closed-shape StreamStateQueryHandler for Rich mode
1a7671daf  [#4411] W4: update SESSION-STATE for compaction-resume
f97840458  [#4411] W4 read-side: simplify reader-columns wiring + add round-trip test
04415a33e  [#4411] W4 read-side O2: closed-shape ApplyReaderDataToEvent
4ac9e216b  [#4410] W4 session-state for compaction-resume
a8d829b1d  [#4410] W4: dialect owns descriptor construction; Rich SQL ported
5ee72bd4e  [#4410] W4 foundation: feature flag + adapter + builder
<spike commit, originally on spike/W4-event-storage-hierarchy>
```

The build is clean (0 errors). The closed-shape **Rich-mode write path**
is now wired end-to-end. With:

```csharp
opts.EventGraph.UseClosedShapeStorage = true;
opts.Events.AppendMode = EventAppendMode.Rich;
```

`StartStream → SaveChangesAsync → FetchStreamStateAsync → FetchStreamAsync`
round-trips identically to the codegen path for both Guid and string
identity (no metadata enabled). Covered by
`Bug_4412_closed_shape_rich_write_path` (two test methods, green on
net9.0 + net10.0). The previous `Bug_4411_closed_shape_read_side` also
remains green.

What still throws:
* Rich + `QuickAppendEventWithVersion` — needs a `SequenceServerSideBinder`
  + `nextval()` SQL fragment + RETURNING read-back. Lands with #4413
  follow-up.
* Quick / QuickWithServerTimestamps modes — their write paths are still
  stubbed (#4414 / #4415).
* `EnableStrictStreamIdentityEnforcement = true` — CTE variant rejects
  at descriptor-build time. Port lands as a follow-up of #4412.
* Any Rich-mode `events.Metadata.X` enabled (`headers`, `causation_id`,
  `correlation_id`, `user_name`, `tags`, `is_skipped`) — `SelectRichMetadataBinders`
  throws NotSupportedException for those column names. #4416.

Pre-existing test failures (verified by parent-commit checkout):
* 4 cases of `archiving_events.prevent_append_*`
* 2 cases of `Bug_4246_enable_bigint_events.{bigint_events_is_false_by_default,
  function_uses_int_when_flag_is_false}` — test asserts a default that was
  flipped in v9.

Neither set was caused by W4.

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
├── Querying/
│   └── ClosedShapeStreamStateQueryHandler.cs   ← #4412 QueryForStream
├── Rich/
│   ├── RichEventStorage.cs
│   ├── RichEventStorageDescriptor.cs
│   ├── RichAppendEventOperation.cs            ← #4413 hardened
│   ├── RichInsertStreamOperation.cs           ← #4412
│   └── RichUpdateStreamVersionOperation.cs    ← #4412
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

#4412 and #4413 (Rich-mode write path) are **landed** (commit
`342806b52`):

* `RichInsertStreamOperation` + `RichUpdateStreamVersionOperation` —
  minimal `IStorageOperation` shells delegating to
  descriptor-installed `Action<ICommandBuilder, StreamAction>` closures.
* `PostgresEventStoreDialect.BuildInsertStreamCommandConfigurer` —
  four-way closure (Guid/string × conjoined/single). Strict-identity
  CTE variant rejects at descriptor-build time (follow-up).
* `BuildUpdateStreamVersionCommandConfigurer` — single closure with
  conditional tenant-id segment.
* `RichAppendEventOperation` rewritten with correct column ordering
  matching the dialect's SQL prefix; Guid + string identity supported
  via `IsGuidStreamIdentity` descriptor flag; `timestamp` inlined as a
  core column.
* `SequenceColumnBinder` fixed for Rich/Full mode (binds
  `Event.Sequence` as a parameter; the QuickWithVersion server-side
  variant lands later).
* `RichEventStorage<TId>.{InsertStream, UpdateStreamVersion, AppendEvent,
  QueryForStream}` all wired.
* `Bug_4412_closed_shape_rich_write_path` proves end-to-end round-trip
  for both Guid and string identity.

**Next concrete unit of work:** [#4414 / #4415](https://github.com/JasperFx/marten/issues/4414)
— Quick / QuickWithServerTimestamps mode hardening. Quick paths use the
`mt_quick_append_events` Postgres function (returns a long[] of seq + version
pairs per event) rather than per-event inserts. The hand-written sample
ops in `EventStorage/Quick/` + `EventStorage/QuickWithServerTimestamps/`
are sketches; the SQL is TODO-stubbed on the dialect. Approach:

1. Port `EventDocumentStorageGenerator.buildQuickAppendOperation` →
   composes `select <schema>.mt_quick_append_events(<args>)` with a
   configuration-aware argument list. The `serverTimestamps` flag
   toggles inclusion of the per-batch timestamp array.
2. Port the array-parameter binding helpers from
   `QuickAppendEventsOperationBase` so the operation can build per-column
   `NpgsqlParameter[]` arrays from the stream's `Events` list.
3. Port the Postprocess loop that walks the returned long[] and assigns
   `Sequence` + (for QuickWithServerTimestamps) `Timestamp` back onto
   each event.
4. Wire `InsertStream` / `UpdateStreamVersion` / `QueryForStream` into
   `QuickEventStorage` / `QuickWithServerTimestampsEventStorage` —
   identical SQL shape to Rich, so the dialect closures can be reused
   (extract a shared helper).
5. Extend `Bug_4412_closed_shape_rich_write_path` (rename, or new
   `Bug_4414_*`) to cover Quick and QuickWithServerTimestamps modes.

**Alternative:** if the Rich path is the priority for v9 ship, the
metadata-binder follow-ups (#4416) — wiring `causation_id`,
`correlation_id`, `headers`, `user_name`, `tags`, `is_skipped`, and
threading the serializer for HeadersColumn read-back — are independent
of the Quick paths and could land next.

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
