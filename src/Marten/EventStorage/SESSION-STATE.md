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

## Landed so far (19 commits on the branch)

```
4b8e033f4  [#4417] W4: pin closed-shape + DCB HStore mode regression test
cde8f0520  [#4417 #4418] W4 SESSION-STATE: suite sweep complete (1496/1501)
ea1c93561  [#4417 #4418] W4 suite-sweep: harness hook + 2 closed-shape fixes
9521858c8  [#4414 #4415] W4 SESSION-STATE: all three AppendModes landed
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
configuration.

```csharp
opts.EventGraph.UseClosedShapeStorage = true;
// AppendMode = QuickWithServerTimestamps (v9 default), Quick, or Rich
```

**Suite sweep results** under `MARTEN_USE_CLOSED_SHAPE_STORAGE=true`
(net10.0):
* Closed-shape regression (Bug_4411 / _4412 / _4414 / _4415 / _4416 / _4417): **15 / 15**
* EventSourcingTests non-Projections/Aggregation/Daemon slice: **957 / 962**
* Projections: **169 / 169**
* Aggregation + Daemon: **245 / 245**
* DCB (incl. all 57 HStore-mode tests): **112 / 112**

Total: **1498 / 1503 passing (99.67%)**.

The 5 remaining failures break down as:
* 3 still-investigating cases (possibly flaky DCB concurrency from the
  first run; not reproducible in isolation — see commit ea1c93561).
* 2 known-deferred:
  `strict_stream_identity_enforcement.*_throws_when_strict_enforcement_enabled(usePartitioning: True)`
  — the `EnableStrictStreamIdentityEnforcement` CTE variant of
  InsertStream isn't ported (open #4412 follow-up). The
  `usePartitioning: False` cases pass through the unique-constraint path.

To run the suite under the closed-shape flag yourself:

```bash
MARTEN_USE_CLOSED_SHAPE_STORAGE=true ./build.sh test-event-sourcing
```

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

* **#4417 / #4418 (suite sweep)** — `Marten.Testing.Harness` gained a
  `TestsSettings.UseClosedShapeStorage` env-var-driven flag and five
  per-context hooks that flip `opts.EventGraph.UseClosedShapeStorage =
  true` after the test's own configure callback when
  `MARTEN_USE_CLOSED_SHAPE_STORAGE=true` is set. Running the full
  event-sourcing suite under the flag surfaced two fixes:
  - `HeadersColumnBinder`: switched from `AppendParameter(DBNull.Value,
    NpgsqlDbType.Jsonb)` (which Npgsql refuses with typed-DBNull) to
    `AppendParameter<object>(DBNull.Value)` + `WriteToParameter`,
    matching codegen.
  - `Quick*EventStorage.AppendEvent`: was throwing; now wired to
    Full-mode per-event INSERT for the tombstone / direct-AppendEvent
    paths that call it regardless of AppendMode. Quick descriptors
    gained `AppendEventFullSqlSuffix` / `AppendEventFullMetadataBinders`.

**Next concrete unit of work:** [#4419](https://github.com/JasperFx/marten/issues/4419)
— migration guide + EventStorage README + draft PR. The closed-shape
path is feature-complete enough to open a draft PR for review:

1. Write `src/Marten/EventStorage/README.md` (renamed from SESSION-STATE;
   strip the developer-facing per-session log, keep the design rationale
   and the configuration / coverage matrix).
2. Update `docs/migration-guide.md` with the v9→v10→v11 trajectory of
   `UseClosedShapeStorage` (default off → default on → codegen removed).
3. Open the draft PR with the suite-sweep results, known limitations,
   and the 5 closed-shape regression tests as the headline proof points.

**Alternative work, in rough priority order:**

* #4412 follow-up: port the `EnableStrictStreamIdentityEnforcement` CTE
  variant of InsertStream. Currently rejects at descriptor-build for
  Rich; Quick paths fall through without the CTE. Fixes the remaining
  2 suite failures. ~80 LOC; one new closure shape in the dialect.
* #4413 follow-up: `RichEventStorage.QuickAppendEventWithVersion` (still
  throws). The Quick paths have this; Rich doesn't. The `RichEventAppender`
  calls `AppendEvent` per-event, not `QuickAppendEventWithVersion`, so
  no current test path hits this. Likely safe to leave unimplemented in
  v9 — would only be exercised by code that interleaves Rich + Quick
  sub-flows.
* SPIKE.md cleanup pass: remove the "spike sample" comments now that the
  classes are production code. The "Hot-path budget" comment in
  `RichAppendEventOperation` is one good example to update.

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
