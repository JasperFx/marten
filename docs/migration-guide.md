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
| `JasperFx.RuntimeCompiler` | 1.x | 2.0.0-alpha.x |
| `Weasel.Postgresql` | 8.x | 9.0.0-alpha.x |
| `Npgsql` | 9.x | 10.x |

**For most consumers**, picking up the new packages happens transitively when you bump `Marten` — no explicit version pins are needed. If your application has explicit references to any of the packages above, bump them in lockstep.

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

### Numeric document revisions widened from `int` to `long`

* The numeric document revision (the value tracked in the `mt_version` column when `UseNumericRevisions` is enabled, or on aggregate documents that derive `Version` from the stream version) is now a 64-bit `long` everywhere. This removes the 2.1B-update-per-document ceiling that `int` imposed — comfortably within reach for high-throughput inline projections and per-event-snapshotted aggregates.

  **What changed on the .NET side:**

  | Surface | Before | After |
  | --- | --- | --- |
  | `Marten.Metadata.IRevisioned.Version` | `int` | `long` |
  | `[Version]`-annotated numeric properties on revisioned documents | `int` | `long` |
  | `DocumentMetadata.CurrentRevision` | `int` | `long` |
  | `IDocumentSession.UpdateRevision<T>(entity, revision)` parameter | `int` | `long` |
  | `IDocumentSession.TryUpdateRevision<T>(entity, revision)` parameter | `int` | `long` |
  | `IRevisionedOperation.Revision` | `int` | `long` |
  | `MartenRegistry.MetadataConfig.Revision` | `Column<int>` | `Column<long>` |

  **What you have to change in your code:**

  * Any class implementing `IRevisioned`: widen `Version` to `long`.

    ```csharp
    // Before (Marten 8)
    public class Reservation : IRevisioned
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
    }

    // After (Marten 9)
    public class Reservation : IRevisioned
    {
        public Guid Id { get; set; }
        public long Version { get; set; }
    }
    ```

  * Any document with a `[Version]`-annotated numeric property used with `UseNumericRevisions`: widen the property to `long`.
  * Any `m.Revision.MapTo(x => x.SomeProperty)` configuration: the target property must be `long`. Mapping to an `int` property now throws `ArgumentOutOfRangeException` at mapping time.
  * Any code calling `IDocumentSession.UpdateRevision` / `TryUpdateRevision` with an explicit `int` literal compiles unchanged (`int` is implicitly convertible to `long`); explicit `int` locals passed in widen with a one-character edit.

  **Schema migration is automatic and non-destructive.** Existing Marten 8 deployments have an `integer` `mt_version` column. Marten 9's schema migration emits `ALTER TABLE … ALTER COLUMN mt_version TYPE bigint` and rewrites the associated `mt_upsert_*` / `mt_update_*` / `mt_overwrite_*` functions to accept and return `BIGINT`. All existing revision values are preserved — there is no data loss and no manual SQL to run.

  **Bulk insert** of a revisioned document type pre-loads expected-version values as `bigint`. If you have a custom `IBulkLoader<T>` implementation (rare), you must use `NpgsqlDbType.Bigint` instead of `NpgsqlDbType.Integer` for the expected-version column.

  See [#3733](https://github.com/JasperFx/marten/issues/3733) / [#4377](https://github.com/JasperFx/marten/pull/4377).

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

#### **`Events.EnableAdvancedAsyncTracking` — flip deferred for 9.0**

* **Stays at `false` (the V8 default) for now.** The intent is to flip this to `true` in 9.0, but turning it on caused large portions of the EventSourcing and Daemon test suites to hang. Tracked in [#4425](https://github.com/JasperFx/marten/issues/4425); the flip lands once that's root-caused.
* **Opt in early:** `opts.Events.EnableAdvancedAsyncTracking = true;` (at your own risk until #4425 is resolved). `RestoreV8Defaults()` does not touch this setting.
* See the [Async Projection Daemon](events/projections/async-daemon.md) docs.

#### **`Events.EnableEventSkippingInProjectionsOrSubscriptions` now defaults to `true`**

* **Was `false` in Marten 8.x.** Enables marking individual events as "plain bad" so a stuck projection or subscription can skip them on subsequent attempts instead of jamming the shard.
* **Restore the V8 default:** `opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = false;` (or `opts.RestoreV8Defaults();`).
* See the [Async Projection Daemon](events/projections/async-daemon.md) docs.

#### **`Events.UseIdentityMapForAggregates` now defaults to `true` — read carefully**

* **Was `false` in Marten 8.x.** This optimizes inline aggregate projections by keeping a session-local identity map of in-flight aggregates so multiple events in the same `SaveChangesAsync` resolve against a single aggregate instance.
* **⚠️ Behavior change risk if your code self-mutates aggregates.** The optimization assumes you obtain aggregates via `IDocumentSession.Events.FetchForWriting()` and use the *decider pattern* (event-handler methods return events; the aggregate is rebuilt from them) rather than mutating fields directly inside the projection's `Apply` methods. If your aggregate handlers self-mutate the aggregate instance, mutations will leak across events within the same batch under the new default and you can see corrupted projections or incorrect optimistic-concurrency comparisons.
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

### AOT / codegen behavior

Marten 9 takes its first real steps toward an AOT-friendly publishing flow. Three changes worth knowing about:

#### **`StoreOptions.AllowRuntimeCodeGeneration` flag (default `true`)**

* Marten 9 gates the runtime Roslyn compilation path behind a new opt-out flag. Set it to `false` in production to refuse to fall through to Roslyn when a pre-generated type can't be found — the boot fails fast instead of silently triggering the JIT-compilation path that's incompatible with AOT publishing.
* The recommended **AOT-publishing flow** is:
  1. At dev time, run `dotnet run -- codegen write` against a host that boots your `StoreOptions`. Marten writes every generated type Marten needs to your project's `Internal/Generated/` folder.
  2. Commit the generated folder.
  3. In production, set `opts.AllowRuntimeCodeGeneration = false;` (and keep `GeneratedCodeMode = TypeLoadMode.Static` — see below). Boot now refuses to call into Roslyn; a missing generated type throws instead of silently regenerating.
* The default stays `true` so existing applications keep working without code changes — nothing breaks if you don't opt in. See [#4309](https://github.com/JasperFx/marten/issues/4309).

#### **Lazy document-mapping materialization**

* `StorageFeatures._documentMappings` is now populated **per document type, on first session that touches it**, instead of being built eagerly at host-build time. The win is significantly faster boot for applications with hundreds of registered document types where only a handful are actually used per request.
* **Behavioral shift to know about:** validation errors that previously surfaced during `IHost.StartAsync` (bad `[Identity]` attribute placement, conflicting metadata-column policies, etc.) now surface on the **first session that touches the offending document type**. If you relied on host-build to be the canary, add an integration test that exercises every registered document type at least once, or call `store.Storage.BuildAllMappings()` eagerly at boot in production.
* See [#4303](https://github.com/JasperFx/marten/issues/4303).

#### **Internal codegen performance plumbing**

These changes are invisible to consumers but worth noting because they affect cold-start traces:

* **LINQ handler factory caching now goes through `GenericFactoryCache`** so repeated LINQ queries against the same shape don't allocate a fresh handler per call ([#4308](https://github.com/JasperFx/marten/issues/4308)).
* **`GenerationRules` is hand-cloned for projection-specific overrides** instead of mutating a shared instance — each projection's codegen sees its own rules and the others stay untouched ([#4307](https://github.com/JasperFx/marten/issues/4307)).

#### **Source-generated compiled queries (`Marten.SourceGenerator`)** {#source-gen-compiled-queries}

Marten 9 ships a source generator that emits the per-`ICompiledQuery<TDoc, TOut>` scaffolding at compile time instead of via `JasperFx.RuntimeCompiler` at first use. Opt-in is implicit: add the analyzer reference + the assembly attribute and every compiled query in that assembly gets a generator-emitted handler registered with the Marten runtime at module load.

Add to the project that declares your `ICompiledQuery<,>` types:

```xml
<ProjectReference Include="..\Marten.SourceGenerator\Marten.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

And in any file in that assembly:

```csharp
[assembly: JasperFx.JasperFxAssembly]
```

What this gets you:

* **No Roslyn emit at first call.** First invocation of a registered compiled query is ~25× faster than the codegen path (measured on a Stateless shape: 3ms vs 75ms on a warm host).
* **No `FastExpressionCompiler` for the per-query parameter binder.** Generator emits a direct property-read switch — ~31% faster steady-state per call.
* **AOT-publishable for the common cases.** The Roslyn-emit step is fully gone for queries the source-gen path covers.

The runtime falls back to the existing codegen path in three documented cases — these are bug-compatible with V8 behavior, just slower at cold start:

* Plans whose SQL needs an `ICompiledQueryAwareFilter` (string `Contains`/`StartsWith`/`EndsWith`, `HashSet<T>.Contains` with JSONB containment, `Dictionary<,>.ContainsKey`, child-collection JsonPath counts).
* Generic or nested `ICompiledQuery<,>` types.
* Compiled queries declared in an assembly without `[JasperFxAssembly]`.

Tracked at [#4405](https://github.com/JasperFx/marten/issues/4405). Removing the codegen-fallback bridge entirely is a follow-up to that issue.

### Obsolete API sweep

Marten 9 retires obsolete types and members deprecated in Marten 8.x:

* **`StoreOptions.GeneratedCodeMode` is `[Obsolete]`.** Prefer the global `IServiceCollection.CritterStackDefaults()` API, which sets `GeneratedCodeMode` and `AutoCreate` per-environment (`Development` / `Production`) and applies the values across every Critter Stack tool in your application (Marten + [Wolverine](https://wolverinefx.net)) in one place:

  ```csharp
  services.CritterStackDefaults(x =>
  {
      x.Production.GeneratedCodeMode = TypeLoadMode.Static;
      x.Production.ResourceAutoCreate = AutoCreate.None;
      // x.Development.* defaults are sensible; override only if needed.
  });
  ```

  The old per-`StoreOptions` `GeneratedCodeMode` setter still works in Marten 9 (it carries an `[Obsolete]` warning) — it will be **removed in Marten 10**. Migrate now to avoid the build-break later.

* `[Obsolete]` types and members deprecated since Marten 8.x have been retired in Marten 9. If your code compiled in Marten 8.x with `[Obsolete]` warnings against any Marten type, those members are now gone in Marten 9 — fix the warnings on 8.x first, then upgrade.

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
| `Events.EnableEventSkippingInProjectionsOrSubscriptions` | `false` — [Async Projection Daemon](events/projections/async-daemon.md) |
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
        // EnableEventSkippingInProjectionsOrSubscriptions, UseIdentityMapForAggregates,
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
    x.Production.GeneratedCodeMode = TypeLoadMode.Static;
    x.Production.ResourceAutoCreate = AutoCreate.None;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#L56-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithcustomsessioncreation' title='Start of snippet'>anchor</a></sup>
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
* The ["pre-built code generation" feature](/configuration/prebuilding) has a new, easier to use option in V5
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
* The dynamic code in Marten moved to a runtime code generation model. If this is causing you any issues with cold start times or memory usage due to Roslyn misbehaving (this is **not** consistent), there is the new ["generate ahead model"](/configuration/prebuilding) as a workaround.
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
