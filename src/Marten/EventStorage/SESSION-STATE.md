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

## Landed so far (15 commits on the branch)

```
499c24575  [#4414 #4415] W4 Quick + QuickWithServerTimestamps write path
0a4d7c58d  [#4416] W4: pin is_skipped contract + clarify SESSION-STATE
cecab0229  [#4416] W4 SESSION-STATE: Rich-mode feature-complete for default + scalar metadata + headers
3d2389599  [#4416] W4 part 2: Headers closed-shape read via ISerializer threading
9a50df363  [#4416] W4: scalar metadata-column binders (causation_id, correlation_id, user_name, headers)
ce6e13f25  [#4412 #4413] W4 SESSION-STATE: Rich write-path landed, Quick paths next
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

The build is clean (0 errors). **All three AppendModes — Rich, Quick,
QuickWithServerTimestamps — now work end-to-end under the closed-shape
flag.** The v9 default (`UseClosedShapeStorage = true` +
`AppendMode = QuickWithServerTimestamps`) is a working drop-in
configuration. With:

```csharp
opts.EventGraph.UseClosedShapeStorage = true;
// AppendMode = QuickWithServerTimestamps (v9 default), Quick, or Rich
// optionally:
opts.Events.MetadataConfig.CausationIdEnabled = true;
opts.Events.MetadataConfig.CorrelationIdEnabled = true;
opts.Events.MetadataConfig.UserNameEnabled = true;
opts.Events.MetadataConfig.HeadersEnabled = true;
```

`StartStream → SaveChangesAsync → FetchStreamStateAsync → FetchStreamAsync`
round-trips identically to the codegen path for both Guid and string
identity, with every metadata field surviving the round trip. Covered by:
* `Bug_4411_closed_shape_read_side` (2 methods)
* `Bug_4412_closed_shape_rich_write_path` (2 methods)
* `Bug_4414_closed_shape_quick_write_path` (3 methods)
* `Bug_4415_closed_shape_quick_with_server_timestamps` (2 methods)
* `Bug_4416_closed_shape_metadata_binders` (4 methods)

Total: **13 closed-shape tests green on net9.0 + net10.0**.

What still throws / not yet wired:
* `EnableStrictStreamIdentityEnforcement = true` — CTE variant rejects
  at descriptor-build time in Rich path. Port lands as a follow-up of
  #4412 (the Quick paths don't go through the strict-identity CTE; the
  function handles identity-enforcement on the server side).
* DCB tag arrays — Quick path's `writeAllTagValues` helper is called,
  but the `TagTypes` collection wiring and operations aren't covered by
  closed-shape tests yet. Likely "just works" if tag types are
  registered; needs verification.
* `is_skipped` — plain `TableColumn`; set to `FALSE` server-side, no
  client-side handling needed (closed-shape regression test in
  Bug_4416 pins this).

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
│   ├── QuickAppendEventsOperation.cs           ← #4414 (function call)
│   ├── QuickAppendEventWithVersionOperation.cs ← #4414 (per-event INSERT)
│   ├── QuickInsertStreamOperation.cs           ← #4414
│   └── QuickUpdateStreamVersionOperation.cs    ← #4414
├── QuickWithServerTimestamps/
│   ├── QuickWithServerTimestampsEventStorage.cs
│   ├── QuickWithServerTimestampsEventStorageDescriptor.cs
│   ├── QuickAppendEventsWithServerTimestampsOperation.cs ← #4415
│   ├── QuickWithServerTimestampsInsertStreamOperation.cs ← #4415
│   └── QuickWithServerTimestampsUpdateStreamVersionOperation.cs ← #4415
└── Metadata/
    ├── HeadersColumnBinder.cs                  ← Rich-mode binder (write)
    ├── SequenceColumnBinder.cs                 ← Rich-mode binder
    ├── CausationIdColumnBinder.cs              ← #4416 part 1
    ├── CorrelationIdColumnBinder.cs            ← #4416 part 1
    └── UserNameColumnBinder.cs                 ← #4416 part 1
```

Also touched outside the new directory:

* `src/Marten/Events/EventGraph.cs` — added `UseClosedShapeStorage` flag
* `src/Marten/Events/EventGraph.GeneratesCode.cs` — wired flag check

## Where to resume

**Landed**, in dependency order:

* **#4411 (read-side O2)** — `IEventTableColumn.ReadValueSync/Async`
  on every concrete column type; closed-shape adapter walks
  `EventsTable.SelectColumns().Skip(3)`.
* **#4412 (per-stream ops)** — `RichInsertStreamOperation`,
  `RichUpdateStreamVersionOperation`, `ClosedShapeStreamStateQueryHandler`.
  Strict-identity CTE variant rejects at descriptor-build (follow-up).
* **#4413 (Rich AppendEventOperation hardening)** — correct column
  ordering, Guid + string identity, `IsGuidStreamIdentity` descriptor
  flag, `SequenceColumnBinder` fixed for Rich/Full mode.
* **#4416 part 1 (scalar metadata binders)** — `CausationIdColumnBinder`,
  `CorrelationIdColumnBinder`, `UserNameColumnBinder` + write-side
  wiring of `HeadersColumnBinder`. Dialect's `SelectRichMetadataBinders`
  now accepts these column names.
* **#4416 part 2 (Headers closed-shape read)** — serializer-aware
  overload added to `IEventTableColumn` (defaults to delegating to the
  parameterless versions). `HeadersColumn` overrides it to deserialize
  jsonb via `ISerializer.FromJson<Dictionary<string,object>>`. Adapter
  threads `_serializer` through both Apply* methods.

* **#4414 (Quick mode)** — `QuickEventStorage<TId>` now wires
  `QuickAppendEvents` (via the `mt_quick_append_events` function call),
  `QuickAppendEventWithVersion` (per-event INSERT with server-side
  `nextval(...)` seq_id), `InsertStream`, `UpdateStreamVersion`,
  `QueryForStream`. Quick descriptor gained `HasCausationId` /
  `HasCorrelationId` / `HasHeaders` / `HasUserName` / `HasTagWrites`
  flags + the closures shared with Rich for per-stream ops + the
  per-event QuickWithVersion SQL prefix/suffix + filtered metadata
  binder array (no SequenceColumnBinder — seq_id is server-set).
* **#4415 (QuickWithServerTimestamps mode)** —
  `QuickWithServerTimestampsEventStorage<TId>` mirrors Quick + the
  operation calls `writeTimestamps(pb)` for the extra timestamp-array
  parameter. The per-event QuickWithVersion path is identical to Quick
  (reuses `QuickAppendEventWithVersionOperation`).

**Next concrete unit of work:** [#4417 / #4418](https://github.com/JasperFx/marten/issues/4417)
— configuration matrix sweep + full event-sourcing suite under the
closed-shape flag. The headline path works (13 closed-shape tests
green); the question now is whether ALL existing event-sourcing tests
pass with the flag globally flipped on. Approach:

1. Add a "closed-shape parallel" run config — `DISABLE_TEST_PARALLELIZATION=true`
   plus a way to set `UseClosedShapeStorage = true` for every test that
   constructs a store. Either a new test harness flag or an environment
   variable the harness reads.
2. Run `./build.sh test-event-sourcing` with the flag on; triage failures.
3. Likely failure categories: (a) DCB tag types — verify the
   `HasTagWrites` path works; (b) `EnableStrictStreamIdentityEnforcement`
   — currently rejects, may need the CTE variant; (c) `RichEventStorage`'s
   `QuickAppendEventWithVersion` — still throws (only Rich's
   non-QuickWithVersion path is wired).

**Alternative work, in rough priority order:**

* #4413 follow-up: `RichEventStorage.QuickAppendEventWithVersion`. The
  Quick paths now both have this; Rich still throws. The
  `RichEventAppender` calls `AppendEvent` (not `QuickAppendEventWithVersion`)
  per-event, so this is only needed for cases where Rich runs alongside
  some QuickWithVersion sub-flow. Verify whether any test actually hits
  this; might be safe to leave unimplemented.
* #4412 follow-up: port the `EnableStrictStreamIdentityEnforcement` CTE
  variant. Currently rejects at descriptor-build for Rich; Quick paths
  delegate enforcement to the server function so they sidestep this.
* #4419: migration guide, EventStorage README, draft PR. The closed-shape
  path is now feature-complete enough that a draft PR + design-doc-style
  README would be useful for early review.

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
