# Migration Guide

## Key Changes in 9.0.0

### Platform support

* **.NET 8 support was dropped.** Marten 9 targets `net9.0` and `net10.0`. Stay on Marten 8.x if you still need .NET 8.
* The solution file format changed from `.sln` to the new XML-based `.slnx`. No action required for consumers — this is purely an internal repo change.

### Critter Stack dependency adoption (JasperFx 2.0 / Weasel 9.0)

Marten 9 is the Marten side of the [Critter Stack 2026](https://github.com/JasperFx/jasperfx/issues/217) release wave. The whole stack of shared dependencies bumped to new major versions in lockstep:

| Package | Marten 8 | Marten 9 |
| --- | --- | --- |
| `JasperFx` | 1.x | 2.0.0-alpha.x |
| `JasperFx.Events` | 1.x | 2.0.0-alpha.x |
| `JasperFx.RuntimeCompiler` | 1.x | *retired* |
| `Weasel.Postgresql` | 8.x | 9.0.0-alpha.x |
| `Npgsql` | 9.x | 10.x |

**For most consumers**, picking up the new packages happens transitively when you bump `Marten` — no explicit version pins are needed. If your application has explicit references to any of the packages above, bump them in lockstep.

`JasperFx.RuntimeCompiler` (the Roslyn-driven runtime code-generation engine) is no longer a Marten dependency. See [Runtime code generation removed](#runtime-code-generation-removed) below for what replaced each surface and what (if anything) you need to do.

**Two structural changes ride along** that you may have to react to:

* Types Marten previously owned that overlapped Weasel / JasperFx contracts have moved to those upstream libraries. See the [Schema dedup audit relocations](#schema-dedup-audit-relocations) section below.
* Some interfaces JasperFx.Events used to leave for Marten to define were lifted upstream. If your code touched `IEventStoreOperations` or `IProjectionCoordinator` and you also `using JasperFx.Events.*`, you'll see CS0104 ambiguous-reference errors — add a `using` alias to the Marten variant in the affected file:

  ```csharp
  using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;
  ```

### Schema dedup audit relocations

Three types Marten core previously owned moved to the shared Weasel / JasperFx packages so all the Critter Stack tools could converge on a single definition. The relocated types keep their public shapes; only the fully-qualified namespace changes.

* **`Marten.Internal.Operations.OperationRole` → `Weasel.Core.OperationRole`.** Third-party consumers that referenced the Marten-side type need to add `using Weasel.Core;` and drop the Marten-side `using` (or qualify inline). Tracked in [#4350](https://github.com/JasperFx/marten/issues/4350) / merged via [#4352](https://github.com/JasperFx/marten/pull/4352).
* **`Marten.BulkInsertMode` → `Weasel.Core.BulkInsertMode`.** Same migration story — bare type name unchanged; `using` directives need to be updated. Audit row at [weasel#264](https://github.com/JasperFx/weasel/issues/264).
* **`IStorageOperation` refactor.** Marten's `IStorageOperation` now `extends Weasel.Core.IStorageOperation` and the **synchronous `Postprocess(...)` overload has been removed** (Npgsql 10 no longer supports the synchronous path). Third-party implementers of `IStorageOperation` must drop their sync override and move that logic into `PostprocessAsync` — there is no rewrite-on-the-fly shim. Tracked in [#4351](https://github.com/JasperFx/marten/issues/4351) / PR [#4353](https://github.com/JasperFx/marten/pull/4353).

### Streams table cleanup

* The `snapshot` (`jsonb`) and `snapshot_version` (`integer`) columns on `mt_streams` have been removed. They were vestigial holdovers from pre-1.0 Marten and were never written or read at runtime — the table simply carried two empty columns on every event store database.

  Marten 9's automatic schema migration will **not** drop these columns from existing databases (we don't drop columns automatically as a safety policy). If you want to reclaim the space, run the following once per event-store schema after upgrading:

  ```sql
  ALTER TABLE my_schema.mt_streams DROP COLUMN snapshot;
  ALTER TABLE my_schema.mt_streams DROP COLUMN snapshot_version;
  ```

  This is purely cosmetic — leaving the columns in place is harmless. New databases created by Marten 9 will not have them. See [#4316](https://github.com/JasperFx/marten/issues/4316).

### `IAggregateGrouper<T>.Group` parameter type tightened

* The `events` parameter on `IAggregateGrouper<T>.Group(...)` changed from `IEnumerable<IEvent>` to `IReadOnlyList<IEvent>`. Implementations frequently need two or more passes over the same batch — partition events by type first, then resolve related document IDs from the database — and the prior `IEnumerable<IEvent>` signature gave no guarantee that re-iteration was safe or cheap. Static analysers correctly flagged it as possible-multiple-enumeration, forcing every implementor to either eat the warning or do a defensive `.ToList()` at the top of `Group`.

  Update the parameter type in your `Group` implementations and drop any defensive `events.ToList()` / `events as IReadOnlyCollection<IEvent>` materialization — `Count`, indexed access, and repeat iteration are first-class on `IReadOnlyList<IEvent>`. No logic change required. The same change applies to the lambda-form `CustomGrouping(Func<IQuerySession, IReadOnlyList<IEvent>, IEventGrouping<TId>, Task>)` overload; lambda call sites usually need no edit because `IReadOnlyList<IEvent>` is also an `IEnumerable<IEvent>` and type inference handles the rest.

  See [jasperfx#201](https://github.com/JasperFx/jasperfx/issues/201) / [jasperfx#202](https://github.com/JasperFx/jasperfx/pull/202).

### Composite projections now expose a single bundled `ShardName`

* Composite projections (`opts.Projections.CompositeProjectionFor("Trips", x => x.Add<A>().Add<B>().Add<C>())`) used to surface one `ShardName` per sub-projection through `ISubscriptionSource.ShardNames()`. In Marten 9 / JasperFx.Events 2.0 they collapse to a single bundled name shaped `<projection-name>/all/v<Version>` — e.g. `trips/all/v2` for a versioned composite with three sub-projections at version 2.
* **Why.** The composite is a coordination boundary: its stages run sequentially against a shared `IProjectionBatch`, so the daemon must hold the whole composite on one node. Per-sub-projection shard names invited two separate nodes to race on the same composite's state under HotCold distribution.
* **Visible impact.** Any code that iterates `usage.Subscriptions.SelectMany(x => x.ShardNames)` to build agent URIs / per-shard tasks (Wolverine 5's `EventSubscriptionAgentFamily` is the canonical example) now sees `N → 1` per composite. Test expectations that count `subscription.ShardNames.Count == subProjectionCount` need to flip to `1`. Downstream distribution code that needs the per-sub-projection list should walk `CompositeProjection.AllProjections()` instead of fanning out via `ShardNames`.
* **Restore the V8 fan-out?** No — there's no `RestoreV8Defaults()` toggle for this. Composite shards stay collapsed; the per-sub-projection view is exposed structurally via `AllProjections()`.

See [#4440](https://github.com/JasperFx/marten/issues/4440).

### `IInlineProjection.ApplyAsync` widened to `IEnumerable<StreamAction>`

* The `streams` parameter on `IInlineProjection.ApplyAsync(IDocumentSession, ..., CancellationToken)` widened from `IReadOnlyList<StreamAction>` to `IEnumerable<StreamAction>`. The internal `RichEventAppender` / `QuickEventAppender` callers now hand the inline-projection pipeline a streaming view of the unit-of-work's streams instead of materializing a list per `SaveChangesAsync`.
* **What you have to change.** Anyone with a custom `IInlineProjection` implementation must update the signature. If you previously relied on `Count` / indexed access on the parameter, materialize once at the top of your `ApplyAsync` body:

  ```csharp
  public Task ApplyAsync(IDocumentSession session, IEnumerable<StreamAction> streams, CancellationToken ct)
  {
      var batch = streams as IReadOnlyCollection<StreamAction> ?? streams.ToList();
      // ...
  }
  ```

* See [#4306](https://github.com/JasperFx/marten/issues/4306).

### `IRevisioned` stays `int`; new `ILongVersioned` for 64-bit revisions

::: warning Reversal since the early 9.0 alphas
An early Marten 9 alpha widened `IRevisioned.Version` from `int` to `long`. **That was reverted before the 9.0 release candidate** (see [#4533](https://github.com/JasperFx/marten/pull/4533)). `IRevisioned.Version` is back to **`int`** — the Marten 8 signature. If you read an earlier alpha guide and already widened your `IRevisioned` documents to `long`, switch them to `ILongVersioned` (below) rather than leaving them on `IRevisioned`.
:::

* **`IRevisioned` is unchanged from Marten 8 — there is no migration to do for ordinary revisioned documents.** `IRevisioned.Version` is `int`. An ordinary per-document revision counter rarely approaches the `int` ceiling, so this is the right default.

* **New: `ILongVersioned` (`long Version`).** Implement this instead of `IRevisioned` when the version is the global **event sequence number** — e.g. a document produced by a `MultiStreamProjection` — which can exceed `Int32.MaxValue`. A `MultiStreamProjection`-derived document that implements `IRevisioned` (int) overflows on the `bigint → int` read once its version passes `Int32`; `ILongVersioned` avoids that. Both interfaces opt the document into numeric revisioning and share the same `bigint` `mt_version` column — only the .NET member width differs. (See [#4526](https://github.com/JasperFx/marten/issues/4526) / [#4528](https://github.com/JasperFx/marten/issues/4528) and JasperFx [#348](https://github.com/JasperFx/jasperfx/issues/348).)

  ```csharp
  // Ordinary revisioned document — UNCHANGED from Marten 8 (int is correct, no edit needed)
  public class Reservation : IRevisioned
  {
      public Guid Id { get; set; }
      public int Version { get; set; }
  }

  // MultiStreamProjection document whose Version is the global event sequence number
  public class CustomerSummary : ILongVersioned
  {
      public Guid Id { get; set; }
      public long Version { get; set; }   // long avoids Int32 overflow at high event counts
  }
  ```

* **The `[Version]` attribute works on `int` or `long`.** A `[Version]`-annotated property used with `UseNumericRevisions` may be either type; no change is required, and you may use `long` if you need the wider range.

* **The underlying column is `bigint`, and a few internal surfaces are `long`.** Independent of which interface you implement, the `mt_version` column is `bigint`, and Marten tracks the revision internally as a 64-bit value. The following are `long` (they were `int` in Marten 8):

  | Surface | Marten 8 | Marten 9 |
  | --- | --- | --- |
  | `Marten.Metadata.IRevisioned.Version` | `int` | `int` (unchanged) |
  | `JasperFx.ILongVersioned.Version` (new) | — | `long` |
  | `DocumentMetadata.CurrentRevision` | `int` | `long` |
  | `IDocumentSession.UpdateRevision<T>(entity, revision)` parameter | `int` | `long` |
  | `IDocumentSession.TryUpdateRevision<T>(entity, revision)` parameter | `int` | `long` |
  | `IRevisionedOperation.Revision` | `int` | `long` |
  | `MartenRegistry` metadata config `m.Revision` | `Column<int>` | `Column<long>` |

  These widenings are source-compatible for almost all callers: an `int` argument is implicitly convertible to `long`, so existing calls to `UpdateRevision` / `TryUpdateRevision` compile unchanged, and `m.Revision.MapTo(...)` accepts an `int` *or* `long` member. The only place you might touch code is a custom `IRevisionedOperation` implementation (a rare, advanced extensibility hook), where `Revision` is now `long`.

  **Schema migration is automatic and non-destructive.** Existing Marten 8 deployments have an `integer` `mt_version` column. Marten 9's schema migration emits `ALTER TABLE … ALTER COLUMN mt_version TYPE bigint` and rewrites the associated `mt_upsert_*` / `mt_update_*` / `mt_overwrite_*` functions to accept and return `BIGINT`. All existing revision values are preserved — there is no data loss and no manual SQL to run.

  **Bulk insert** of a revisioned document type pre-loads expected-version values as `bigint`. If you have a custom `IBulkLoader<T>` implementation (rare), you must use `NpgsqlDbType.Bigint` instead of `NpgsqlDbType.Integer` for the expected-version column.

### Optional HSTORE-backed DCB tag storage

* Marten 9 adds an opt-in alternative storage layout for [DCB](events/dcb.md) tags: `DcbStorageMode.HStore`. The default (`DcbStorageMode.TagTables`) is unchanged, so **no migration is required** when upgrading from Marten 8 — existing tag tables and queries continue to work exactly as before.

  ```csharp
  // Marten 8 / Marten 9 default — one Postgres table per registered tag type
  opts.Events.RegisterTagType<StudentId>("student");

  // Marten 9 opt-in — all tags live inline on mt_events.tags (hstore)
  // with a single GIN index covering every tag type
  opts.Events.DcbStorageMode = DcbStorageMode.HStore;
  opts.Events.RegisterTagType<StudentId>("student");
  ```

  When to consider opting in:

  * Your DCB queries usually match on **two or more tag types** — the JOIN-free HStore mode is ~90% faster on the common `QueryByTagsAsync(2 tags OR)` shape and ~70% faster on `EventsExistAsync(2 tags OR)`.
  * `FetchForWritingByTags` is on your hot path — round-trip drops by roughly half.
  * Your schema is dominated by tag tables and the proliferation is becoming a maintenance burden.

  When to stay on `TagTables`:

  * Your DCB workload is dominated by **single-tag `EventsExistAsync` probes** — HStore is slightly slower on that specific case.
  * You already have a populated TagTables-mode store. The mode is chosen per database at creation time; in-place migration between modes is not provided.
  * Your Postgres deployment doesn't allow the `hstore` extension to be installed.

  The full trade-off table and measured per-op numbers live in the [DCB documentation → Choosing a Storage Mode](events/dcb.md#choosing-a-storage-mode) section. See [#4238](https://github.com/JasperFx/marten/issues/4238) / [#4379](https://github.com/JasperFx/marten/pull/4379).

### Flipped defaults in Marten 9 — read this section

Marten 9 ships a handful of `StoreOptions` defaults flipped to the values recommended for a greenfield project in [Jeremy's "Building a Greenfield System with the Critter Stack" post](https://jeremydmiller.com/2026/02/02/building-a-greenfield-system-with-the-critter-stack/). The full set is summarized in the [Restoring V8 Defaults](#restoring-v8-defaults) section at the end — call `opts.RestoreV8Defaults()` to revert every flip in one line if you're upgrading an existing Marten 8 application and not ready to opt in piecemeal. Each individual flip is detailed below.

#### **`Events.AppendMode` now defaults to `EventAppendMode.QuickWithServerTimestamps`**

* **Was `EventAppendMode.Rich` in Marten 8.x.** The `Quick`/`QuickWithServerTimestamps` append path delivers roughly 50% higher throughput and reduces event-skipping under contention; `QuickWithServerTimestamps` is preferred over `Quick` because it preserves database-side timestamps that most applications rely on.
* **Restore the V8 default:** `opts.Events.AppendMode = EventAppendMode.Rich;` (or `opts.RestoreV8Defaults();`).
* See the [Event Appending modes](events/appending.md) and [Optimizing the Event Store](events/optimizing.md) docs.

#### **`Events.EnableAdvancedAsyncTracking` now defaults to `true`**

* **Was `false` in Marten 8.x.** Records high-water skips into `mt_high_water_skips` so the async daemon can reason about gaps it has already skipped past instead of re-detecting them on every poll. See [#4425](https://github.com/JasperFx/marten/issues/4425) for the bootstrap bug that previously blocked this flip.
* **Restore the V8 default:** `opts.Events.EnableAdvancedAsyncTracking = false;` (or `opts.RestoreV8Defaults();`).
* See the [Async Projection Daemon](events/projections/async-daemon.md) docs.

#### **`Events.UseIdentityMapForAggregates` now defaults to `true` — read carefully**

* **Was `false` in Marten 8.x.** This optimizes inline aggregate projections by keeping a session-local identity map of in-flight aggregates so multiple events in the same `SaveChangesAsync` resolve against a single aggregate instance.
* **⚠️ Behavior change risk if your code self-mutates aggregates.** The optimization assumes you obtain aggregates via `IDocumentSession.Events.FetchForWriting()` and use the *decider pattern* (event-handler methods return events; the aggregate is rebuilt from them) rather than mutating fields directly inside the projection's `Apply` methods. If your aggregate handlers self-mutate the aggregate instance, mutations will leak across events within the same batch under the new default and you can see corrupted projections or incorrect optimistic-concurrency comparisons.
* **Concrete failure mode (Wolverine `[AggregateHandler]` pattern, [#4439](https://github.com/JasperFx/marten/issues/4439) / [#4509](https://github.com/JasperFx/marten/issues/4509)):** a handler that bumps `aggregate.ACount` to compute a `Response` and then returns `new AEvent()` ends up persisting `ACount + 2` (mutation **plus** the event's `ACount++` apply) instead of `ACount + 1`, so the persisted snapshot diverges from the `AggregateStreamAsync` rebuild. The mutation flows through the identity-map cache that `FetchForWriting` populates, so the inline projection's next apply loop starts from the mutated value instead of the database snapshot. Returning events without touching the fetched aggregate (the decider pattern), or setting this flag to `false`, both restore the V8 contract.
* **What to do:** Either (a) migrate your aggregate handlers to the decider pattern + `FetchForWriting`, or (b) keep the V8 default explicitly: `opts.Events.UseIdentityMapForAggregates = false;` (or call `opts.RestoreV8Defaults();`).
* See the [Aggregate Projections](events/projections/aggregate-projections.md) and [FetchForWriting](events/projections/aggregate-projections.md#rehydrating-aggregates-for-writes) docs.

#### **`Events.EnableBigIntEvents` now defaults to `true`**

* **Was `false` in Marten 8.x.** Switches the `mt_quick_append_events` and `mt_get_next_hi` PostgreSQL functions to use `bigint` (64-bit) for event version, sequence, and hi-lo return values. Eliminates the ~2.1B-events overflow ceiling that 32-bit columns imposed.
* **⚠️ Schema impact.** Marten 9's automatic schema migration alters the relevant columns and function signatures from `integer` to `bigint`. The migration is data-preserving (all existing sequence values are kept verbatim — `bigint` is a strict superset of `integer`) and runs once on first boot. Existing rows are not rewritten.
* **Restore the V8 default:** `opts.Events.EnableBigIntEvents = false;` (or `opts.RestoreV8Defaults();`).
* See the [Event Store](events/index.md) docs.

#### **`DisableNpgsqlLogging` now defaults to `true`**

* **Was `false` in Marten 8.x.** Suppresses the (very noisy) Npgsql-internal logger that V8 forwarded to your `ILogger<Marten>`. Marten's own structured logs are unaffected.
* **Restore the V8 default:** `opts.DisableNpgsqlLogging = false;` (or `opts.RestoreV8Defaults();`).
* See the [StoreOptions](configuration/storeoptions.md) reference.

### Default `IDocumentSession` from DI is now lightweight

* When you call `services.AddMarten(...)` and inject `IDocumentSession`, Marten 9 hands you a **lightweight session** by default. Marten 8 returned an **identity-map session**.
* **Lightweight sessions do not de-duplicate loaded documents within a session.** If your V8 code relied on `await session.LoadAsync<T>(id)` returning the same instance across repeated calls within a single session, you'll see distinct instances after upgrading. The same applies to documents loaded into queries and aggregates.
* **What to do:** If you depend on identity-map behavior, restore it on the DI side — `RestoreV8Defaults()` on `StoreOptions` cannot reach the DI session factory:
  ```csharp
  services.AddMarten(opts =>
  {
      opts.Connection(connectionString);
      // opts.RestoreV8Defaults();  // restores StoreOptions defaults only
  })
  .UseIdentitySessions();          // <-- restores the V8 DI default
  ```
* See [Document Sessions](documents/sessions.md) for the full session-type comparison.

### Default serializer is now `System.Text.Json`

* Marten 8 used `Newtonsoft.Json` by default. Marten 9 uses `System.Text.Json` by default, and the Newtonsoft integration moved to a **separate `Marten.Newtonsoft` NuGet package**. Marten core no longer depends on `Newtonsoft.Json`.
* **What to do if you want the V8 Newtonsoft default back:**
  1. Add the `Marten.Newtonsoft` NuGet package: `dotnet add package Marten.Newtonsoft`.
  2. Add `using Marten.Newtonsoft;` at the call site.
  3. Call `opts.UseNewtonsoftForSerialization(...)` (now an extension method) yourself — `RestoreV8Defaults()` does not touch the serializer, by design (it cannot reach across the package boundary).
* See [JSON Serialization](configuration/json.md) for the full migration details and per-serializer trade-offs.

### Runtime code generation removed {#runtime-code-generation-removed}

Marten 9.0 retires the `JasperFx.RuntimeCompiler` (Roslyn) dependency and all the surfaces that used to compile C# at first use. Every replacement is in place by 9.0.0; the path is not opt-in.

What was replaced:

| Surface | Pre-9.0 | Marten 9 |
| --- | --- | --- |
| `IDocumentStorage<T, TId>` | Roslyn-emitted subclass per document type, one per `StorageStyle` | Hand-written closed-shape hierarchy in `Marten.Internal.ClosedShape`, parameterized by a reflection-built `IIdentification<TDoc, TId>` ([#4404](https://github.com/JasperFx/marten/issues/4404)) |
| `IEventStorage` write path | Roslyn-emitted `GeneratedEventDocumentStorage` | Hand-written `RichEventStorage<TId>` / `QuickEventStorage<TId>` / `QuickEventWithServerTimestampsStorage<TId>` adapted by `ClosedShapeEventDocumentStorage` ([#4410](https://github.com/JasperFx/marten/issues/4410)) |
| `IEventStorage` read path (`ApplyReaderDataToEvent`) | Roslyn-emitted selector | `IEventTableColumn.ReadValueSync/Async` runtime delegates ([#4411](https://github.com/JasperFx/marten/issues/4411)) |
| Compiled queries | Roslyn-emit at first use, with optional `dotnet run -- codegen write` pre-generation | `Marten.SourceGenerator` (compile-time) + a `FastExpressionCompiler`-built descriptor as the runtime fallback ([#4405](https://github.com/JasperFx/marten/issues/4405)) |
| `AddMartenStore<T>()` secondary stores | Roslyn-emitted `class TImplementation : DocumentStore, T` | `System.Reflection.Emit` proxy in `SecondaryStoreProxyFactory` |
| `JasperFx.RuntimeCompiler` `PackageReference` | required | **deleted** from `Marten.csproj` |

What that means for application code:

* **No more `dotnet run -- codegen write` step for Marten.** Pre-built `Internal/Generated/` folders are obsolete — delete them from your project and `.gitignore`. The closed-shape paths build their descriptors at first use (cheap) and cache them; there is no compile-on-cold-start. If your host also runs Wolverine or another JasperFx-family tool, those still ship their own codegen and may still require the `codegen write` step in your Dockerfile — only the Marten portion of the step is now redundant.
* **The codegen-config knobs have been deleted.** `StoreOptions.GeneratedCodeMode`, `StoreOptions.SourceCodeWritingEnabled`, `StoreOptions.GeneratedCodeOutputPath`, and `StoreOptions.AllowRuntimeCodeGeneration` are gone — references to them will fail to compile against Marten 9.0. Remove them from your bootstrapping. `StoreOptions.ApplicationAssembly` is kept (legitimately used by `AutoRegister` and `TryUseSourceGeneratedDiscovery` as a scan hint).
* **The `Pre-Building Generated Types` documentation page has been retired.** Anything that linked to `/configuration/prebuilding` now 404s. The closest equivalent for "I want to ship without dynamic codegen" is reading the [compiled queries source-generator section](#source-gen-compiled-queries) below — the source generator covers the AOT-clean cases the pre-build flow used to.

#### **Lazy document-mapping materialization**

* `StorageFeatures._documentMappings` is now populated **per document type, on first session that touches it**, instead of being built eagerly at host-build time. The win is significantly faster boot for applications with hundreds of registered document types where only a handful are actually used per request.
* **Behavioral shift to know about:** validation errors that previously surfaced during `IHost.StartAsync` (bad `[Identity]` attribute placement, conflicting metadata-column policies, etc.) now surface on the **first session that touches the offending document type**. If you relied on host-build to be the canary, add an integration test that exercises every registered document type at least once, or call `store.Storage.BuildAllMappings()` eagerly at boot in production.
* See [#4303](https://github.com/JasperFx/marten/issues/4303).

#### **Source-generated compiled queries (`Marten.SourceGenerator`)** {#source-gen-compiled-queries}

The compile-time path is the supported way to use compiled queries in Marten 9. Opt-in is implicit: add the analyzer reference + the assembly attribute and every compiled query in that assembly gets a generator-emitted handler registered with the Marten runtime at module load.

Add to the project that declares your `ICompiledQuery<,>` types:

```xml
<PackageReference Include="Marten.SourceGenerator" PrivateAssets="all" />
```

And in any file in that assembly:

```csharp
[assembly: JasperFx.JasperFxAssembly]
```

What this gets you:

* **No reflection at the per-call hot path.** Generator emits a direct property-read switch — ~31% faster steady-state per call than the runtime fallback.
* **AOT-publishable for the common cases.** No dynamic-codegen surface for queries the generator covers.

Queries the generator can't see at build time fall through to a reflection + `FastExpressionCompiler`-built descriptor cached in the same `CompiledQueryHandlerRegistry`. The fallback covers:

* Plans whose SQL needs an `ICompiledQueryAwareFilter` (string `Contains`/`StartsWith`/`EndsWith`, `HashSet<T>.Contains` with JSONB containment, `Dictionary<,>.ContainsKey`, child-collection JsonPath counts).
* Generic or nested `ICompiledQuery<,>` types.
* Compiled queries declared in an assembly without `[JasperFxAssembly]`.

The fallback is reflective, not Roslyn — there's no per-query compilation; it's a one-shot `FastExpressionCompiler` setup at first call. Tracked at [#4405](https://github.com/JasperFx/marten/issues/4405).

#### **Closed-shape event storage** {#closed-shape-event-storage}

The hand-written event-storage hierarchy is the only event-store write path in 9.0:

* The append / insert-stream / update-stream-version / stream-state-query operations are concrete hand-written classes parameterized by per-`EventGraph` descriptors built once at `DocumentStore` construction. The runtime never branches on `AppendMode` after startup.
* Adding a new metadata column is now an `IEventMetadataBinder` implementation plus a dialect-method case; no codegen template-tweaking.
* The `MARTEN_USE_CLOSED_SHAPE_STORAGE` env-var sweep that ran in 9.0-alpha is gone — closed-shape is the default.

Architecture overview lives in [`src/Marten/EventStorage/README.md`](https://github.com/JasperFx/marten/tree/master/src/Marten/EventStorage) for contributors adding new metadata binders or dialects.

### Inline-lambda projection registration removed {#inline-lambda-projection-removal}

Coordinates with [JasperFx/jasperfx#286](https://github.com/JasperFx/jasperfx/issues/286). The inline-lambda registration APIs on the projection / aggregator base classes still rely on FastExpressionCompiler-compiled delegates because the source generator cannot statically discover handlers that are passed as runtime values. Those APIs are gone in the JasperFx 2.0 line that Marten 9 picks up:

| Removed | Replacement |
| --- | --- |
| `SingleStreamProjection<T, TId>.ProjectEvent<TEvent>(...)` (all 7 overloads) | `Apply` / `Evolve` method convention on the projection class |
| `SingleStreamProjection<T, TId>.CreateEvent<TEvent>(...)` | `Create` method convention |
| `SingleStreamProjection<T, TId>.DeleteEvent<TEvent>(...)` (all overloads) | `ShouldDelete` method convention, or `Evolve` returning `null` |
| `EventProjection.Project<TEvent>(action)` | `Project` method convention on the projection class |
| `EventProjection.ProjectAsync<TEvent>(action)` | `ProjectAsync` method convention on the projection class |

The replacement pattern is to convert the inline-lambda body into a conventional method on a `partial` projection class. `JasperFx.Events.SourceGenerator` discovers the methods at compile time and emits a `[GeneratedEvolver]` dispatcher with no runtime reflection — the same path Marten already uses for projections registered the conventional way today.

**Before — inline lambdas:**

```csharp
public class OrderProjection : SingleStreamProjection<Order, Guid>
{
    public OrderProjection()
    {
        ProjectEvent<OrderPlaced>((order, e) => order.Apply(e));
        ProjectEvent<OrderShipped>((order, e) => order.Shipped = e.ShippedAt);
        DeleteEvent<OrderCancelled>();
        DeleteEvent<OrderArchived>((order, _) => order.Status == "Closed");
    }
}
```

**After — convention methods on a partial class:**

```csharp
public partial class OrderProjection : SingleStreamProjection<Order, Guid>
{
    public Order Apply(OrderPlaced e, Order order) => order.Apply(e);

    public void Apply(OrderShipped e, Order order) => order.Shipped = e.ShippedAt;

    public bool ShouldDelete(OrderCancelled e) => true;

    public bool ShouldDelete(OrderArchived e, Order order) => order.Status == "Closed";
}
```

The same shape applies to `EventProjection` — replace `Project<TEvent>(action)` / `ProjectAsync<TEvent>(action)` with `Project` / `ProjectAsync` method-convention overloads on a `partial` projection class.

The `partial` keyword on the class is what lets the source generator emit a sibling partial declaration with the `[GeneratedEvolver]` dispatcher. Existing convention-based projections that don't currently use `partial` keep working without it (Marten falls back to runtime evolver lookup for those); adding `partial` is what enables the AOT-clean and trim-clean dispatcher path. See [Runtime code generation removed](#runtime-code-generation-removed) for the broader context on Marten 9's source-generated dispatch model.

If you need to delete the aggregate based on async work (the equivalent of the removed `DeleteEventAsync` overload), implement an `async`-returning `ShouldDelete` method that takes an `IQuerySession`:

```csharp
public async Task<bool> ShouldDelete(Breakdown e, Trip trip, IQuerySession session)
{
    var anyRepairShopsInState = await session.Query<RepairShop>()
        .Where(x => x.State == trip.State)
        .AnyAsync();

    return !anyRepairShopsInState;
}
```

The doc pages that previously showed the inline-lambda examples ([`/events/projections/conventions`](/events/projections/conventions), [`/events/projections/single-stream-projections`](/events/projections/single-stream-projections), [`/events/projections/event-projections`](/events/projections/event-projections), [`/events/projections/flat`](/events/projections/flat)) carry warning callouts pointing back here. The samples in those pages still reference the old API today — they will be migrated when the JasperFx 2.0 GA cut lands and the API is physically removed.

### Aggregation method visibility now required to be `public` {#aggregation-public-handlers}

Marten 8 and earlier used runtime reflection to dispatch events to `Apply` / `Create` / `ShouldDelete` methods on your aggregate or projection class. The reflection path picked up `private`, `internal`, and `protected` handlers — handlers were free to be encapsulated.

Marten 9 routes aggregation through a compile-time source generator (`JasperFx.Events.SourceGenerator`) that emits direct method calls into a sibling `partial` class. Generated code can only invoke `public` members of the user's type, so the visibility requirement tightens: **all conventional handler methods on aggregates and projection classes must be `public`** in Marten 9.

| Affected method shape | Pre-9.0 reflection | Marten 9 SG |
| --- | --- | --- |
| `private void Apply(SomeEvent e)` on aggregate | dispatched | not dispatched (silently) |
| `internal bool ShouldDelete(SomeEvent e)` | dispatched | not dispatched (silently) |
| `private SomeAggregate(SomeEvent e)` (event-shaped ctor) | dispatched as Create | not dispatched (use a `public` ctor) |
| `private SomeAggregate()` (parameterless ctor for rehydration) | dispatched via `Activator.CreateInstance(nonPublic: true)` | the SG falls back to `RuntimeHelpers.GetUninitializedObject(typeof(T))` — **field initializers on the aggregate type are not invoked** in that fallback. Move field initialization into Apply / Create or make the ctor `public`. |

**Migration:** flip the visibility of any private / internal / protected `Apply` / `Create` / `ShouldDelete` methods (and event-shaped constructors) to `public`.

```csharp
// Before — pre-9.0, worked via reflection:
public sealed class Invoice : AggregateBase
{
    private Invoice() { }
    public Invoice(int invoiceNumber)
    {
        var @event = new InvoiceCreated(invoiceNumber);
        Apply(@event);
        AddUncommittedEvent(@event);
    }
    private void Apply(InvoiceCreated e) { /* ... */ }
    private void Apply(LineItemAdded e)  { /* ... */ }
}

// After — Marten 9, dispatched via the source generator:
public sealed class Invoice : AggregateBase
{
    public Invoice() { }                       // public parameterless for replay
    public Invoice(int invoiceNumber) { /* ... */ }
    public void Apply(InvoiceCreated e) { /* ... */ }
    public void Apply(LineItemAdded e)  { /* ... */ }
}
```

This rule applies to:

* `Apply` / `Create` / `ShouldDelete` methods on aggregates registered via `opts.Projections.Snapshot<T>(...)` or used live via `theSession.Events.AggregateStreamAsync<T>(streamId)` and friends.
* `Apply` / `Create` / `ShouldDelete` methods on `SingleStreamProjection<TDoc, TId>` / `MultiStreamProjection<TDoc, TId>` subclasses.
* `Project` / `ProjectAsync` methods on `EventProjection` subclasses.
* Event-shaped constructors (`public T(SomeEvent e)`) on aggregates — the SG now treats these as implicit Create handlers in Marten 9, but only when the ctor is `public`.

If you need encapsulation for your aggregate state, the standard pattern of `public` getters + `private set` properties still works — only the *methods* and *constructors* that Marten dispatches to need to be `public`.

### Identity-by-attribute on non-`Id` members {#aggregation-identity-attribute}

If your aggregate uses an `[Identity]`-marked property whose name isn't `Id` (e.g. a `[Identity] public string StreamKey` member), Marten 9 now respects that attribute at compile time when generating the dispatcher — no source change required:

```csharp
public record LoadTestInlineProjection
{
    [Identity]
    public string StreamKey { get; init; }   // recognized as the aggregate identity in 9.0
    public LoadTestInlineProjection Apply(LoadTestEvent e, LoadTestInlineProjection current) => /* ... */;
}
```

Aggregates that use a runtime override via `opts.Schema.For<T>().Identity(x => x.SomeMember)` are **not** visible to the source generator (it can't see runtime configuration at compile time) — annotate the member with `[Identity]` instead, or expose it as the `Id` property.

### Required-member aggregates {#aggregation-required-members}

Aggregates whose root type declares `required` members are now supported as projection roots in Marten 9. The source generator constructs the empty instance via `new T { RequiredA = default!, RequiredB = default! }` and immediately runs the user's first Apply on it — your Apply is expected to overwrite those `default!` values:

```csharp
public class ExternalAccountLink
{
    public required string Id        { get; set; }
    public required Guid   CustomerId { get; set; }
}

public partial class ExternalAccountLinkProjection : SingleStreamProjection<ExternalAccountLink, string>
{
    public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
    {
        link.Id         = e.ExternalAccountId;
        link.CustomerId = e.CustomerId;
    }
}
```

If you'd prefer not to rely on the `default!` placeholder, add a `public static T Create(SomeEvent e)` method on the aggregate — the SG will route the null-snapshot branch through `Create` instead.

### Validation-rule behavior change {#aggregation-validation-rules}

A few of the runtime-validation messages that Marten 8 threw at registration time are no longer emitted, because the source generator silently skips signatures it can't dispatch:

* **Unrecognized method names** on a projection class (anything not named `Apply` / `Create` / `ShouldDelete`) used to throw `InvalidProjectionException`. Marten 9 silently ignores them. Use `[JasperFxIgnore]` (still honored) or rename the method.
* **Projection-class `Apply` without the aggregate parameter** (`public void Apply(SomeEvent e)` on a `SingleStreamProjection<TDoc, TId>`) used to throw. Marten 9 dispatches it but the method can't mutate aggregate state because the aggregate isn't in scope. Add the aggregate parameter back.
* **`SingleStreamProjection` targeting a soft-deleted document type** used to throw at `ValidateConfiguration` time. The source-generated dispatcher doesn't know about the document's soft-delete config and is no longer in a position to detect the conflict at registration.

### Synchronous query APIs removed

**Marten 9 fully removes the synchronous data-access path.** Every database-bound synchronous LINQ terminal operator, `IQuerySession`/`IDocumentSession` sync helper, and the `IQueryHandler<T>.Handle(DbDataReader, IMartenSession)` extensibility hook now either no longer exist or throw at runtime:

```text
NotSupportedException: As of Marten 9.0, only asynchronous data access is supported
```

Async-only was the recommended path for several releases — the sync methods have been carrying an `[Obsolete]` warning since Marten 7. They're gone now.

**What you have to change:**

* Replace every sync LINQ terminal operator on a Marten `IQueryable<T>` with its async equivalent (the message above will surface at runtime if you miss one):

  | Before (Marten 8 — `[Obsolete]`) | After (Marten 9) |
  | --- | --- |
  | `session.Query<Foo>().ToList()` | `await session.Query<Foo>().ToListAsync()` |
  | `session.Query<Foo>().ToArray()` | `(await session.Query<Foo>().ToListAsync()).ToArray()` |
  | `session.Query<Foo>().First()` | `await session.Query<Foo>().FirstAsync()` |
  | `session.Query<Foo>().FirstOrDefault()` | `await session.Query<Foo>().FirstOrDefaultAsync()` |
  | `session.Query<Foo>().Single()` | `await session.Query<Foo>().SingleAsync()` |
  | `session.Query<Foo>().SingleOrDefault()` | `await session.Query<Foo>().SingleOrDefaultAsync()` |
  | `session.Query<Foo>().Count()` | `await session.Query<Foo>().CountAsync()` |
  | `session.Query<Foo>().LongCount()` | `await session.Query<Foo>().LongCountAsync()` |
  | `session.Query<Foo>().Any()` | `await session.Query<Foo>().AnyAsync()` |
  | `session.Query<Foo>().Min(x => x.N)` | `await session.Query<Foo>().MinAsync(x => x.N)` |
  | `session.Query<Foo>().Max(x => x.N)` | `await session.Query<Foo>().MaxAsync(x => x.N)` |
  | `session.Query<Foo>().Sum(x => x.N)` | `await session.Query<Foo>().SumAsync(x => x.N)` |
  | `session.Query<Foo>().Average(x => x.N)` | `await session.Query<Foo>().AverageAsync(x => x.N)` |
  | `foreach (var f in session.Query<Foo>())` | `await foreach (var f in session.Query<Foo>().ToAsyncEnumerable(ct))` |

* Replace every `Load<T>`, `LoadMany<T>`, `Query<T>(sql, ...)`, `Json.*` sync call on `IQuerySession` with the corresponding `LoadAsync`, `LoadManyAsync`, `QueryAsync`, `Json.*Async` equivalent.

* The sync `IQueryable<T>.ToPagedList(pageNumber, pageSize)` extension method (and the matching `PagedList<T>.Create(...)` / `PagedList<T>.Init(...)`) now throw the same `NotSupportedException` because their implementations called sync `LongCount()` / `ToList()` underneath. Switch to `await queryable.ToPagedListAsync(pageNumber, pageSize, token)` (the async variant has been there since Marten 4).

* If you implement `IQueryHandler<T>` (a fairly advanced extensibility hook used by user-supplied SQL plans and custom selectors), the interface no longer has a synchronous `Handle(DbDataReader reader, IMartenSession session)` method — implement only `HandleAsync(DbDataReader, IMartenSession, CancellationToken)`. Delete any existing sync `Handle` override; it isn't satisfying anything anymore.

There is **no escape hatch.** The internal `QuerySession.ExecuteHandler<T>(IQueryHandler<T>)` overload remains for now to preserve the type surface but throws the same `NotSupportedException` — use `ExecuteHandlerAsync(handler, cancellationToken)` instead. If you have a code path that genuinely needs blocking execution (e.g. interop with a non-async legacy framework), wrap the async call with `.GetAwaiter().GetResult()` at your own risk.

See [#4420](https://github.com/JasperFx/marten/issues/4420).

### Obsolete API sweep

Marten 9 retires obsolete types and members deprecated in Marten 8.x:

* **`StoreOptions.GeneratedCodeMode` and the codegen-config family have been deleted** (see [Runtime code generation removed](#runtime-code-generation-removed)). The `x.Production.GeneratedCodeMode = TypeLoadMode.Static;` line that appeared in 8.x `CritterStackDefaults` samples is no longer relevant to Marten — drop it from your bootstrapping. The `ResourceAutoCreate` half of `CritterStackDefaults` is unchanged:

  ```csharp
  services.CritterStackDefaults(x =>
  {
      x.Production.ResourceAutoCreate = AutoCreate.None;
      // x.Development.* defaults are sensible; override only if needed.
  });
  ```

* `[Obsolete]` types and members deprecated since Marten 8.x have been retired in Marten 9. If your code compiled in Marten 8.x with `[Obsolete]` warnings against any Marten type, those members are now gone in Marten 9 — fix the warnings on 8.x first, then upgrade.

### Renames coordinated with JasperFx 2.0 / JasperFx.Events 2.0

The JasperFx 2.0 wave (`JasperFx` 2.0.0-alpha.16+, `JasperFx.Events` 2.0.0-alpha.15+) finishes a set of coordinated `[Obsolete]` removals. After upgrading Marten 9, audit consumer code for these renames:

* **`ProjectionBase.ProjectionName` → `Name`.** If your projection subclass overrides or sets the legacy property in its constructor, switch to `Name`.
* **`ProjectionBase.ProjectionVersion` → `Version`.** Same shape — find/replace any setter or getter usage.
* **`JasperFxSubscriptionBase.SubscriptionName` → `Name`** and **`JasperFxSubscriptionBase.SubscriptionVersion` → `Version`.** Mirrors the projection rename for subscription subclasses.
* **`EventSlice<T>.Aggregate` → `Snapshot`** (also on `IEventSlice<T>`). The old name was a holdover from a pre-1.0 vocabulary; semantics are unchanged.
* **`MessageMetadata.LastModifiedBy` / `IMetadataContext.LastModifiedBy` → `CurrentUserName`.** The session-level "current user name" property was renamed for clarity. `Marten.IDocumentSession.LastModifiedBy` and `Marten.Storage.Metadata.DocumentMetadata.LastModifiedBy` are **separate document-side properties** (the "who last modified this row" stored column) and continue to use `LastModifiedBy` — only the session-level reading of the current user changed.
* **`IEventStore.TeardownExistingProjectionProgressAsync` removed.** The full-state teardown helper `IEventStore.TeardownExistingProjectionStateAsync` (identical signature, takes `IEventDatabase`, the subscription/projection name, and a `CancellationToken`) is the replacement. The progress-only variant always also tore down the projected document state in practice, so the consolidated method matches actual behavior. Custom `IEventStore` implementations need their explicit interface implementation updated.
* **`MultiStreamProjection.CustomGrouping(IEventSlicer<TDoc, TId, TQuerySession>)` removed.** Pass a `Func<TQuerySession, IReadOnlyList<IEvent>, IEventGrouping<TId>, Task>` lambda (the still-supported overload) or an `IAggregateGrouper<TId>` instance instead. Whole-slicer replacement is no longer a supported extension point — express the grouping logic in the lambda body.
* **`Oakton.*` shims removed** (`OaktonEnvironment` / `ApplyOaktonExtensions` / `RunOaktonCommands`). These were `[Obsolete]` in the 1.x line. Drop the `using Oakton;` and switch to `JasperFx.JasperFxEnvironment` / `ApplyJasperFxExtensions` / `RunJasperFxCommands`.

`CombGuidIdGeneration` keeps its `[Obsolete]` for one more cycle — scheduled for a future major release rather than this wave.

### Restoring V8 defaults

Migrating from Marten 8? Call `StoreOptions.RestoreV8Defaults()` first, then layer your own configuration on top:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    // Reverts every `StoreOptions` default that Marten 9 flipped.
    opts.RestoreV8Defaults();

    // ...your usual configuration, document mappings, projections...
});
```

`RestoreV8Defaults()` reverts every setting flipped in this release:

| Setting | V8 default it restores |
| --- | --- |
| `Events.AppendMode` | `EventAppendMode.Rich` — [Event Appending](events/appending.md) |
| `Events.EnableAdvancedAsyncTracking` | `false` — [Async Projection Daemon](events/projections/async-daemon.md) |
| `Events.UseIdentityMapForAggregates` | `false` — [Aggregate Projections](events/projections/aggregate-projections.md) |
| `Events.EnableBigIntEvents` | `false` — [Event Store](events/index.md) |
| `DisableNpgsqlLogging` | `false` — [StoreOptions](configuration/storeoptions.md) |

`RestoreV8Defaults()` **does not** cover two cross-cutting V9 changes — handle them explicitly:

* **Default serializer.** Add the `Marten.Newtonsoft` NuGet package, `using Marten.Newtonsoft;`, and call `opts.UseNewtonsoftForSerialization(...)`. See [JSON Serialization](configuration/json.md).
* **Default injected `IDocumentSession`.** Chain `.UseIdentitySessions()` after `AddMarten(...)`. See [Document Sessions](documents/sessions.md).

### End-to-end migration example

A typical Marten 8 app that wants to upgrade with **zero behavior change** sets every V8 default back and pulls Newtonsoft + identity-map sessions back via the optional package. The full call shape looks like this:

```csharp
// 1. Update package references in your csproj:
//    <PackageReference Include="Marten" Version="9.0.0" />
//    <PackageReference Include="Marten.Newtonsoft" Version="9.0.0" />  <!-- new -->

// 2. Update bootstrap to revert every flipped default + restore V8 wiring:
services.AddMarten(opts =>
    {
        opts.Connection(configuration.GetConnectionString("Marten"));

        // Revert the StoreOptions defaults Marten 9 flipped (AppendMode,
        // EnableAdvancedAsyncTracking, UseIdentityMapForAggregates,
        // EnableBigIntEvents, DisableNpgsqlLogging).
        opts.RestoreV8Defaults();

        // Restore V8's Newtonsoft serializer default (Marten 9 ships STJ by default
        // and Newtonsoft moved to the optional Marten.Newtonsoft package).
        opts.UseNewtonsoftForSerialization();   // <-- using Marten.Newtonsoft;

        // ...your existing options: document mappings, projections, plugin policies...
    })
    // Restore V8's identity-map session default (Marten 9 ships lightweight
    // sessions by default when you inject IDocumentSession from DI).
    .UseIdentitySessions();
```

Once that compiles and your test suite is green, you can opt back into the V9 defaults one at a time — start with the throughput wins (`AppendMode = Quick` / `QuickWithServerTimestamps`) and the low-risk ones (`DisableNpgsqlLogging`), then move on to the behavioral ones (`UseIdentityMapForAggregates` — only after you've migrated to the decider-pattern + `FetchForWriting` flow) when you're ready.

For an application that's adopting V9 fresh and wants every greenfield default, do **not** call `RestoreV8Defaults()` — Marten 9 is already configured the way the [greenfield-defaults post](https://jeremydmiller.com/2026/02/02/building-a-greenfield-system-with-the-critter-stack/) recommends.

## Key Changes in 8.0.0

The V8 release was much smaller than the preceding V7 release, but there are some significant changes to be aware of.

### General

* 8.0 depends on Npgsql 9 and requires Postgres 13+. Postgres 12 is no longer supported.

* Marten 8 drops support for .NET 6 and .NET 7. Only .NET 8 and 9 are supported at the moment (.NET 10 is untested).

* Marten 8 **eliminated almost all synchronous API signatures that result in database calls**. Instead you will need to use
asynchronous APIs. For example, a call to `IQuerySession.Load<MyEntity(id)` will have to be changed to `await IQuerySession.LoadAsync<MyEntity>(id)`.
The only exception is the LINQ `ToList()/ToArray()` type operators that result in making database calls with synchronous
APIs. Due to Npgsql dropping support for sync APIs in Npgsql 10, these APIs will be removed in Marten 9 and throw `NotSupportedException` exceptions asking
you to switch to asynchronous methods instead.

* Nullable Reference Types has been enabled across the entire project which will result in some APIs appearing nullable or non-nullable when they weren't in the past. Please open an issue if you run into incorrect annotations.

* The basic shared dependencies underneath Marten and its partner project [Wolverine](https://wolverinefx.net) were consolidated
for the V8 release into the new, core [JasperFx and JasperFx.Events](https://github.com/jasperfx/jasperfx) libraries. This is
going to cause some changes to your Marten system when you upgrade:

* Some core types like `IEvent` and `StreamAction` moved into the new JasperFx.Events library. Hopefully your IDE can help you change namespace references in your code

* JasperFx subsumed what had been "Oakton" for command line parsing. There are temporarily shims for all the public Oakton types and methods, but from
  this point forward, the core JasperFx library has all the command line parsing and you can pretty well change "Oakton" in your code to "JasperFx"

* The previous "Marten.CommandLine" Nuget was combined into the core Marten library. You will need to remove any explicit references to this Nuget.

* The new projection support in JasperFx.Events no longer uses any code generation for any of the projections. The code generation
for entity types, ancillary document stores, and some internals of the event store still exists unchanged.

* The Open Telemetry span names inside the async daemon do not embed the database identifier in the case of multi-tenancy through separate databases. Instead,
  all projection and subscription activity has the same naming, but the database is a tag on the span if you want to disambiguate the work. 

* If you create a custom implementation of `IProjection` in Marten 8, the projection name is the type name instead of the earlier full name. You may need to override
  the projection name in this case to reflect your older usage.

### Event Sourcing

The projection base classes have minor changes in Marten 8:

* The `SingleStreamProjection` now requires 2 generic type arguments for both the projected document type and the identity type of that document. This compromise was made to better support the increasing widespread usage of strong typed identifiers.

v7: `InvoiceProjection : SingleStreamProjection<Invoice>`

v8: `InvoiceProjection : SingleStreamProjection<Invoice, InvoiceId>`

* Both `SingleStreamProjection` and `MultiStreamProjection` have improved options for writing explicit code for projections for more complex scenarios or if you just prefer that over the conventional `Apply` / `Create` method approach
* `CustomProjection` has been deprecated and marked as `[Obsolete]`! Moreover, it's just a direct subclass of `MultiStreamProjection` now
* There is also an option in `EventProjection` to use explicit code in place of the its conventional usage, and this is the new recommended approach
  for projections that do not fit either of the aggregation use cases (`SingleStream/MultiStreamProjection`)

On the bright side, we believe that the "event slicing" usage in Marten 8 is significantly easier to use than it was before.

### Conventions

The existing "Optimized Artifacts Workflow" was completely removed in V8. Instead though, there is a new option shown below:

<!-- snippet: sample_addmartenwithcustomsessioncreation -->
<a id='snippet-sample_addmartenwithcustomsessioncreation'></a>
```cs
var connectionString = Configuration.GetConnectionString("postgres");

services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
    })

    // Chained helper to replace the built in
    // session factory behavior
    .BuildSessionsWith<CustomSessionFactory>();

// In a "Production" environment, we're turning off the
// automatic database migrations and dynamic code generation
services.CritterStackDefaults(x =>
{
    x.Production.ResourceAutoCreate = AutoCreate.None;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#L55-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithcustomsessioncreation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note the usage of `CritterStackDefaults()` above. This will allow you to specify separate behavior for `Development` time vs
`Production` time for frequently variable settings like the generated code loading behavior or the classic `AutoCreate` setting
for whether or not Marten should do runtime migrations of the database structure. Better yet, these settings are global across
the entire application so that you no longer have to specify the same variable behavior for [Wolverine](https://wolverinefx.net) when using
both tools together. 

## Key Changes in 7.0.0

The V7 release significantly impacted Marten internals and also included support for .NET 8 and and upgrade to Npgsql 8.
In addition, Marten 7.0 requires at least PostgreSQL 12 because of the dependence upon sql/json constructs introduced in PostgreSQL 12.

Marten 7 includes a large overhaul of the LINQ provider support, with highlights including:

* Very significant improvements to querying through document child collections by being able to opt into
  JSONPath or containment operator querying in many cases. Early reports suggest an order of magnitude improvement in
  query times. 
* GIST/GIN indexes should be effective with Marten queries again
* The `IMethodCallParser` interface changed slightly, and any custom implementations will have to be adjusted
* Covers significantly more use cases within the LINQ `Where()` filtering
* `Select()` support was widened to include constructor functions

The database connection lifetime logic in `IDocumentSession` or `IQuerySession` was changed from the original Marten 1-6 "sticky" connection behavior. Instead
of Marten trying to keep a database connection open from first usage through any call to `SaveChangesAsync()`, Marten
is auto-closing the connection on every usage **by default**. This change should help reduce the overall number of 
open connections used at runtime, and help make Marten be more easily integrated into GraphQL solutions using
the [Hot Chocolate framework](https://chillicream.com/docs/hotchocolate/v13). 

See [Connection Handling](/documents/sessions.html#connection-handling) for more information, including how to opt into
the previous V6 and earlier "sticky" connection lifetime. 

Marten 7 replaces the previous `IRetryPolicy` mechanism for resiliency with built in support for Polly. 
See [Resiliency Policies](/configuration/retries) for more information.

## Key Changes in 6.0.0

The V6 release lite motive is upgrading to .NET 7 and Npgsql 7. Besides that, we decided to align the event sourcing projections' naming and initializing document sessions. See the [full release notes](https://github.com/JasperFx/marten/releases/tag/6.0.0).

We tried to limit the number of breaking changes and mark methods with obsolete attributes to promote the new recommended way.

The scope of breaking changes is limited, but we highly encourage migrating from all obsolete usage to the new conventions.

### Guide on migration from v5 to v6:

* **We Dropped support of .NET Core 3.1 and .NET 5** following the [Official .NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy). That allowed us to benefit fully from recent .NET improvements around asynchronous code, performance etc. Plus made maintenance easier by removing branches of code. If you're using those .NET versions, you need to upgrade to .NET 6 or 7.
* **Upgraded Npgsql version to 7.** If your project uses an explicitly lower version of Npgsql than 7, you'll need to bump it. We didn't face substantial issues this time, so you might not need to do around it, but you can double-check in the [Npgsql 7 release notes](https://www.npgsql.org/doc/release-notes/7.0.html#breaking-changes) for detailed information about breaking changes on their side.
* **Generic `OpenSession` store options (`OpenSession(SessionOptions options)` does not track changes by default.** Previously, it was using [identity map](https://martendb.io/documents/sessions.md#identity-map-mechanics). Other overloads of `OpenSession` didn't change the default behavior but were made obsolete. We encourage using explicit session creation and `LightweightSession` by default, as in the next major version, we plan to do the full switch. Read more about the [Unit of Work mechanics](/documents/sessions.md#unit-of-work-mechanics).
* **Renamed asynchronous session creation to include explicit Serializable name.** `OpenSessionAsync` was misleading, as the intention behind it was to enable proper handling of Postgres' serialized transaction level. Renamed the method to `OpenSerializableSessionAsync` and added explicit methods for session types. Check more in [handling Transaction Isolation Level](/documents/sessions.md#enlisting-in-existing-transactions).
* **Removed obsolete methods marked as to be removed in the previous versions.**:
  * Removed synchronous'BuildProjectionDaemon`from the`IDocumentStore` method. Use the asynchronous version instead.
  * Removed `Schema` from `IDocumentStore`. Use `Storage` instead.
  * Replaced `GroupEventRange` in `IAggregationRuntime` with `Slicer` reference.
  * Removed unused `UseAppendEventForUpdateLock` setting.
  * Removed the `Searchable` method from `MartenRegistry`. Use `Index` instead.
    **[ASP.NET JSON streaming `WriteById`](/documents/aspnetcore.md#single-document) is now using correctly custom `onFoundStatus`.** We had the bug and always used the default status. It's enhancement but also technically a breaking change to the behavior. We also added `onFoundStatus` to other methods, so you could specify, e.g. `201 Created` status for creating a new record.
* **Added [Optimistic concurrency checks](/documents/concurrency.md#optimistic-concurrency) during documents' updates.** Previously, they were only handled when calling the `Store` method; now `Update` uses the same logic.
* **Base state passed as parameter is returned from `AggregateStreamAsync` instead of null when the stream is empty.** `AggregateStreamAsync` allows passing the default state on which we're applying events. When no events were found, we were always returning null. Now we'll return the passed value. It is helpful when you filter events from a certain version or timestamp. It'll also be useful in the future for archiving scenarios
* **Ensured events with both `Create` and `Apply` in stream aggregation were handled only once.** When you defined both Create and Apply methods for the specific event, both methods were called for the single event. That wasn't expected behavior. Now they'll be only handled once.
* **Added missing passing Cancellation Tokens in all async methods in public API.** That ensures that cancellation is handled correctly across the whole codebase. Added the static analysis to ensure we won't miss them in the future.
* **All the Critter Stack dependencies like `Weasel`, `Lamar`, `JasperFx.Core`, `Oakton`, and `JasperFx.CodeGeneration` were bumped to the latest major versions.** If you use them explicitly, you'll need to align the versions.

### Besides that, non-breaking but important changes to upgrade are:

* **Added explicit `LightweightSession` and `IdentitySession` creation methods to `DocumentStore`**. Previously you could create `DirtyTrackedSession` explicitly. Now you can create all types of sessions explicitly. We recommend using them explicitly instead of the generic `OpenSession` method.
* **Renamed aggregations into projections and `SelfAggregate` into `Snapshot` and `LiveStreamAggregation`.** The established terms in the Event Sourcing community are Projection and Snapshot. Even though our naming was more precise on the implementation behind the scenes, it could be confusing. We decided to align it with the common naming and be more explicit about the intention. Old methods were marked as obsolete and will be removed in the next major release.

### Other notable new features:

* **[Added support for reusing Documents in the same async projection batch](/events/projections/event-projections.md#reusing-documents-in-the-same-batch).** By default, Marten does batch to handle multiple events for the projection in one update. When using `EventProjection` and updating data manually using `IDocumentOperations`, this may cause changes made for previous batch items not to be visible. Now you can opt-in for tracking documents by an identity within a batch using the `EnableDocumentTrackingByIdentity` async projection option. Read more in [related docs](/events/projections/event-projections.md#reusing-documents-in-the-same-batch).
* **Enabled the possibility of applying projections with different Conjoined Tenancy scopes for projections.** Enabled global projection for events with a conjoined tenancy style. Read more in [multi-tenancy documentation](/documents/multi-tenancy.md)
* **Added automatic retries when schema updates are running in parallel.** Marten locks the schema update using advisory locks. Previously when acquiring lock failed, then schema update also failed. Now it will be retried, which enables easier parallel automated tests and running schema migration during the startup for the containerized environment.

## Key Changes in 5.0.0

V5 was a much smaller release for Marten than V4, and should require much less effort to move from V4 to V5 as it did from V2/3 to V4.

* The [async daemon](/events/projections/async-daemon) has to be explicitly added with a chained call to `AddAsyncDaemon(mode)`
* The [Marten integration with .Net bootstrapping](/getting-started) now has the ability to split the Marten configuration for testing overrides or modular configuration
* `IInitialData` services are executed within IHost bootstrapping. See [Initial Baseline Data](/documents/initial-data).
* New facility to [apply all detected database changes on application startup](/schema/migrations.html#apply-all-outstanding-changes-upfront).
* Ability to [register multiple Marten document stores in one .Net IHost](/configuration/hostbuilder.html#working-with-multiple-marten-databases)
* The "pre-built code generation" feature had a new, easier to use option in V5 (retired in 9.0 — see [Runtime code generation removed](#runtime-code-generation-removed))
* New ["Optimized Artifact Workflow"](/configuration/optimized_artifact_workflow) option
* Some administrative or diagnostic methods that were previously on `IDocumentStore.Advanced` migrated to database specific access [as shown here](/configuration/multitenancy.html#administering-multiple-databases).

## Key Changes in 4.0.0

V4 was a very large release for Marten, and basically every subsystem was touched at some point. When you are upgrading from V2/3 to V4 -- and even
earlier alphas or RC releases of 4.0 -- you will need to run a [database migration](/schema/migrations) as part of your migration to V4.

Other key, breaking changes:

* All schema management methods, including assertions on the schema, are now asynchronous. We had to do this for Npgsql connection multiplexing.
* The [compiled query](/documents/querying/compiled-queries) syntax changed
* The [event store](/events/) support has quite a few additions
* [Projections](/events/projections/) in Marten have moved to an all new programming model. Some of it is at least similar, but read the documentation on projection types before moving a Marten application over
* The [async daemon](/events/projections/async-daemon) was completely rewritten, and is now about to run in application clusters and handle multi-tenancy
* A few diagnostic methods moved within the API
* Document types need to be public now, and Marten will alert you if document types are not public
* The dynamic code in Marten moved to a runtime code generation model. (Marten 9.0 retired that path entirely — see [Runtime code generation removed](#runtime-code-generation-removed).)
* If an application bootstraps Marten through the `IServiceCollection.AddMarten()` extension methods, the default logging in Marten is through the standard
  `ILogger` of the application
* In order to support more LINQ query permutations, LINQ queries are temporarily not using the GIN indexable operators on documents that have `GinIndexJsonData()` set. Support for this can be tracked [in this GitHub issue](https://github.com/JasperFx/marten/issues/2051)
* PLV8 support is disabled by default and moved to a separate package.
  If an application was setting `StoreOptions.PLV8Enabled = false` to disable PLV8,
  that line should be removed as the setting no longer exists. If an application
  had `StoreOptions.PLV8Enabled = true` and was using PLV8, you will need to add
  the `Marten.PLv8` package.

## Key Changes in 3.0.0

Main goal of this release was to accommodate the **Npgsql 4.\*** dependency.

Besides the usage of Npgsql 4, our biggest change was making the **default schema object creation mode** to `CreateOrUpdate`. Meaning that Marten even in its default mode will not drop any existing tables, even in development mode. You can still opt into the full "sure, I’ll blow away a table and start over if it’s incompatible" mode, but we felt like this option was safer after a few user problems were reported with the previous rules. See [schema migration and patches](/schema/migrations) for more information.

We also aligned usage of `EnumStorage`. Previously, [Enum duplicated fields](/documents/indexing/duplicated-fields) was always stored as `varchar`. Now it's using setting from `JsonSerializer` options - so by default it's `integer`. We felt that it's not consistent to have different default setting for Enums stored in json and in duplicated fields.

See full list of the fixed issues on [GitHub](https://github.com/JasperFx/marten/milestone/26?closed=1).

You can also read more in [Jeremy's blog post from](https://jeremydmiller.com/2018/09/27/marten-3-0-is-released-and-introducing-the-new-core-team/).

## Migration from 2.\*

* To keep Marten fully rebuilding your schema (so to allow Marten drop tables) set store options to:

```csharp
AutoCreateSchemaObjects = AutoCreate.All
```

* To keep [enum fields](/documents/indexing/duplicated-fields) being stored as `varchar` set store options to:

```csharp
DuplicatedFieldEnumStorage = EnumStorage.AsString;
```

* To keep [duplicated DateTime fields](/documents/indexing/duplicated-fields) being stored as `timestamp with time zone` set store options to:

```csharp
DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime = false;
```
