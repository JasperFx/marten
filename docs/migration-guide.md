# Migration Guide

## Key Changes in 9.41.0

### `UseAdvisoryLockTransaction` now defaults to `false`

The async daemon's `HotCold` leadership lock is now session-scoped (`pg_try_advisory_lock`) by
default instead of transaction-scoped (`pg_try_advisory_xact_lock`). The leader no longer keeps a
transaction open on its lock connection for as long as it holds leadership, so that connection stops
showing up as `idle in transaction` in `pg_stat_activity`.

No schema change is involved. The one deployment that needs action is a daemon running behind
PgBouncer in transaction pooling mode, which can hand a session-scoped lock's server connection to
another client. Restore the previous behavior there:

```cs
opts.Events.UseAdvisoryLockTransaction = true;
```

See [Solo vs. HotCold](/events/projections/async-daemon#solo-vs-hotcold) for details.

## Key Changes in 9.21.0

### Optional function update — `mt_quick_append_events` <Badge type="tip" text="no action required" />

Marten 9.21 changes one line in the `mt_quick_append_events` PostgreSQL function. **This is not a
required migration.** You do not have to patch anything, schedule anything, or coordinate a
deployment window. If you do nothing at all, 9.21 is correct against the function you already have
deployed.

The change fixes [#5062](https://github.com/JasperFx/marten/issues/5062): called with an *empty*
event array, the function returned an array whose single element was `NULL`, because
`array_length('{}', 1)` is `NULL` in PostgreSQL rather than `0`:

```sql
-- before
return_value := ARRAY[event_version + array_length(event_ids, 1)];
-- after
return_value := ARRAY[event_version + COALESCE(array_length(event_ids, 1), 0)];
```

Npgsql could not read that value into `long[]`, and the resulting `InvalidCastException` was thrown
from the batch's post-processing loop — where it displaced whatever exception had actually made the
append fail, leaving callers with an unrelated, non-retryable error.

#### Why you can ignore it

Marten 9.21 also fixes this on the client side, and that fix is the one that matters for existing
deployments: the append operation no longer reads the returned array at all when the batch carries
no events, and the one code path that could call the function with an empty array no longer does.
Those changes ship in the assembly, so **the masking behavior is gone the moment you upgrade the
NuGet package** — regardless of which version of the function your database holds.

#### What you will see, and when

* **Default (`AutoCreate.CreateOrUpdate` or `All`)** — the function is refreshed the next time
  Marten ensures event storage exists. Nothing to do; it is a `CREATE OR REPLACE FUNCTION` with no
  lock on your event data.
* **`AutoCreate.None` with `db-patch` / `db-apply` pipelines** — your next patch will contain one
  extra `CREATE OR REPLACE FUNCTION … mt_quick_append_events` statement. Apply it whenever it suits
  your normal cadence. It is idempotent, and Weasel will stop reporting the delta once applied.

If you would rather not carry a pending delta, generate and apply the patch at your convenience:

```bash
dotnet run -- db-patch ./schema-9.21.sql --drop ./schema-9.21.drop.sql
```

## Key Changes in 9.4.0

### Required schema migration — DCB tag-version side table <Badge type="warning" text="action required" />

If you register **any** DCB tag types via `Events.RegisterTagType<T>()` (or auto-discovery from `SingleStreamProjection<TDoc, TId>` with a strong-typed `TId`), Marten 9.4 introduces a new schema object: a side table `mt_dcb_tag_version` in your event-store schema. The table is created automatically on first save under the default `AutoCreate.CreateOrUpdate`, but **deployments that pin `AutoCreate.None` and ship schema changes via `db-patch` / `db-apply` must run the migration before deploying 9.4**, or saves with tagged events will fail with `relation "<schema>.mt_dcb_tag_version" does not exist`.

```bash
# Generate the patch against your production-equivalent DB
dotnet run -- db-patch ./schema-9.4.sql --drop ./schema-9.4.drop.sql

# Or apply directly if your deploy pipeline runs Marten with elevated DDL rights
dotnet run -- db-apply
```

#### What changes on every save (with DCB tags)

Beyond the new schema object, every save that appends a tagged event now also queues one extra `INSERT … ON CONFLICT DO UPDATE` against `mt_dcb_tag_version`. This is the *producer-side bump* that makes the boundary check serializable. The overhead is one row write per distinct `(tag_type, tag_value)` tuple referenced by the batch — typically one or two rows per save.

#### Why this lands as a required migration in a point release

This is the fix for [#4591](https://github.com/JasperFx/marten/issues/4591) — a correctness bug where truly-concurrent DCB tag-boundary appends could **both commit** when the contract demands exactly one. The check pre-9.4 emitted a `SELECT EXISTS (… FROM mt_events …)` as a separate non-locking statement before the INSERTs at `READ COMMITTED`, leaving an open race window. Two concurrent fetch→save sessions both ran the check before either committed, both saw no conflict, both inserted. The bug affected both `DcbStorageMode.HStore` and `DcbStorageMode.TagTables` — the predicate shape differed but the racy `SELECT-then-INSERT` pattern was identical.

The side-table mechanism converts the predicate read into a row-level write conflict, so concurrent boundary saves serialize on a row lock at `READ COMMITTED` — no `SERIALIZABLE`, no advisory locks. See [DCB → Consistency Check](/events/dcb#consistency-check) for the full mechanism.

#### Growth and cleanup

The side table grows with **distinct boundary-tag values**, not with event volume — the same `StudentId` or `CourseId` reuses its row across every save. Rows are never deleted automatically; avoid using ephemeral or one-shot values as DCB tags if you want to keep the table compact.

## Key Changes in 9.0.0

Marten 9 is a major release. It drops .NET 8, moves to the JasperFx 2.0 and Weasel 9 packages, replaces
runtime code generation with source generators, removes synchronous data access, and changes several
defaults to the values recommended for new projects.

This section has three parts:

* [Upgrading from Marten 8](#upgrading-from-marten-8): the shortest path to running Marten 9 with the
  same behavior you have today.
* [Breaking changes](#breaking-changes-in-9-0-0): everything that can stop your code compiling or change
  how it behaves.
* [New features](#new-features-in-9-0-0): additions you can adopt when you are ready. None of them are
  required to upgrade.

### Upgrading from Marten 8 {#upgrading-from-marten-8}

Most of the behavior changes in Marten 9 are new defaults, and every one of them can be switched back.
The configuration below keeps a Marten 8 application behaving as it did before:

```csharp
// Package references:
//   <PackageReference Include="Marten" Version="9.0.0" />
//   <PackageReference Include="Marten.Newtonsoft" Version="9.0.0" />

services.AddMarten(opts =>
    {
        opts.Connection(configuration.GetConnectionString("Marten"));

        // Reverts the StoreOptions defaults listed below
        opts.RestoreV8Defaults();

        // Newtonsoft is no longer the default serializer (using Marten.Newtonsoft;)
        opts.UseNewtonsoftForSerialization();

        // ...your existing document mappings, projections and policies
    })
    // The injected IDocumentSession is no longer an identity-map session
    .UseIdentitySessions();
```

This does not cover code changes, only defaults. You still need to work through the rest of the
[breaking changes](#breaking-changes-in-9-0-0), in particular
[runtime code generation](#runtime-code-generation-removed),
[projection registration](#inline-lambda-projection-removal) and
[synchronous data access](#synchronous-data-access-removed).

Once your tests pass, adopt the Marten 9 defaults one at a time. `DisableNpgsqlLogging` and `AppendMode`
are the low-risk ones to start with. Leave `UseIdentityMapForAggregates` until your aggregate handlers
follow the decider pattern (see [Changed defaults](#changed-defaults)).

A new application should not call `RestoreV8Defaults()`. Marten 9 is already configured the way the
[greenfield defaults post](https://jeremydmiller.com/2026/02/02/building-a-greenfield-system-with-the-critter-stack/)
recommends.

#### Restoring V8 defaults {#restoring-v8-defaults}

`RestoreV8Defaults()` puts these `StoreOptions` settings back to their Marten 8 values:

| Setting | Marten 8 value | Docs |
| --- | --- | --- |
| `Events.AppendMode` | `EventAppendMode.Rich` | [Event Appending](events/appending.md) |
| `Events.EnableAdvancedAsyncTracking` | `false` | [Async Projection Daemon](events/projections/async-daemon.md) |
| `Events.UseIdentityMapForAggregates` | `false` | [Aggregate Projections](events/projections/aggregate-projections.md) |
| `Events.EnableBigIntEvents` | `false` | [Event Store](events/index.md) |
| `DisableNpgsqlLogging` | `false` | [StoreOptions](configuration/storeoptions.md) |

It cannot reach the serializer, which lives in a separate package, or the DI session factory. That is
why the example above also calls `UseNewtonsoftForSerialization()` and `UseIdentitySessions()`.

#### Schema changes

Marten's automatic schema migration handles all of these. None need manual SQL.

* **`mt_version` becomes `bigint`.** Document tables with numeric revisions are altered from `integer`,
  and the `mt_upsert_*`, `mt_update_*` and `mt_overwrite_*` functions are rewritten to match. Existing
  values are kept.
* **Event sequence and version functions become `bigint`** when `EnableBigIntEvents` is on, which is the
  new default. See [Changed defaults](#changed-defaults).
* **`mt_streams` loses its `snapshot` and `snapshot_version` columns** in new databases. Marten never
  used them. Existing databases keep them, because Marten does not drop columns automatically. They are
  harmless, but you can remove them once per event store schema
  ([#4316](https://github.com/JasperFx/marten/issues/4316)):

  ```sql
  ALTER TABLE my_schema.mt_streams DROP COLUMN snapshot;
  ALTER TABLE my_schema.mt_streams DROP COLUMN snapshot_version;
  ```

### Breaking changes {#breaking-changes-in-9-0-0}

#### Platform and packages

**.NET 8 is no longer supported.** Marten 9 targets `net9.0` and `net10.0`. Stay on Marten 8 if you
need .NET 8.

**The shared Critter Stack packages moved up a major version together** as part of the
[Critter Stack 2026](https://github.com/JasperFx/jasperfx/issues/217) release:

| Package | Marten 8 | Marten 9 |
| --- | --- | --- |
| `JasperFx` | 1.x | 2.x |
| `JasperFx.Events` | 1.x | 2.x |
| `JasperFx.RuntimeCompiler` | 1.x | no longer used |
| `Weasel.Postgresql` | 8.x | 9.x |
| `Npgsql` | 9.x | 9.x |

You get these transitively from `Marten`. If you reference any of them directly, upgrade them at the
same time.

**Some interfaces now exist in both Marten and JasperFx.Events.** A file that uses
`IEventStoreOperations` or `IProjectionCoordinator` and also imports a `JasperFx.Events` namespace fails
with CS0104 (ambiguous reference). Alias the Marten type in that file:

```csharp
using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;
```

#### Changed defaults {#changed-defaults}

These defaults changed to the values recommended for a new project. See
[Upgrading from Marten 8](#upgrading-from-marten-8) to put them all back at once.

| Setting | Marten 8 | Marten 9 | To keep the Marten 8 behavior |
| --- | --- | --- | --- |
| `Events.AppendMode` | `Rich` | `QuickWithServerTimestamps` | `opts.Events.AppendMode = EventAppendMode.Rich` |
| `Events.EnableAdvancedAsyncTracking` | `false` | `true` | `opts.Events.EnableAdvancedAsyncTracking = false` |
| `Events.UseIdentityMapForAggregates` | `false` | `true` | `opts.Events.UseIdentityMapForAggregates = false` |
| `Events.EnableBigIntEvents` | `false` | `true` | `opts.Events.EnableBigIntEvents = false` |
| `DisableNpgsqlLogging` | `false` | `true` | `opts.DisableNpgsqlLogging = false` |
| Injected `IDocumentSession` | identity map | lightweight | `.UseIdentitySessions()` after `AddMarten()` |
| Serializer | Newtonsoft.Json | System.Text.Json | `Marten.Newtonsoft` package and `opts.UseNewtonsoftForSerialization()` |

**`AppendMode`.** The quick append path gives roughly 50% more throughput and fewer skipped events under
contention. `QuickWithServerTimestamps` is the default rather than `Quick` because it keeps the
database-assigned timestamps most applications depend on. See
[Event Appending](events/appending.md) and [Optimizing the Event Store](events/optimizing.md).

**`EnableAdvancedAsyncTracking`.** The async daemon records the sequence gaps it skips in
`mt_high_water_skips`, so it does not have to detect them again on every poll
([#4425](https://github.com/JasperFx/marten/issues/4425)). This adds tables and columns to the event
store schema.

**`UseIdentityMapForAggregates`.** Inline aggregate projections reuse the aggregate instance that
`FetchForWriting()` loaded, instead of loading it again, when applying the events from the same
`SaveChangesAsync()`.

::: warning Aggregates you modify directly
This is only safe if you treat the aggregate from `FetchForWriting()` as read-only and express every
change as an event (the decider pattern). If a handler changes the aggregate directly and also appends an
event, the change is applied twice: once by the handler and once by the projection replaying the event
on the same instance. For example, a Wolverine `[AggregateHandler]` that increments `aggregate.ACount`
and returns an `AEvent` whose `Apply` also increments it persists `ACount + 2`, so the stored snapshot
no longer matches `AggregateStreamAsync()` ([#4439](https://github.com/JasperFx/marten/issues/4439),
[#4509](https://github.com/JasperFx/marten/issues/4509)).

Either stop modifying the fetched aggregate, or set `UseIdentityMapForAggregates = false`. See
[Aggregate Projections](events/projections/aggregate-projections.md) and
[FetchForWriting](events/projections/aggregate-projections.md#rehydrating-aggregates-for-writes).
:::

**`EnableBigIntEvents`.** Event versions, sequences and hi-lo values are 64-bit, removing the limit of
about 2.1 billion events. The schema migration changes the affected columns and functions from `integer`
to `bigint` on first start. Existing values are kept and rows are not rewritten.

**`DisableNpgsqlLogging`.** Npgsql's internal logging is no longer forwarded to your `ILogger`. Marten's
own logging is unaffected.

**Injected `IDocumentSession`.** `AddMarten()` now registers a lightweight session. A lightweight session
does not track loaded documents, so loading the same id twice gives you two different instances. If you
rely on getting the same instance back, chain `.UseIdentitySessions()` after `AddMarten()`. See
[Document Sessions](documents/sessions.md).

**Serializer.** Marten uses System.Text.Json by default and no longer depends on Newtonsoft.Json. The
Newtonsoft integration moved to the `Marten.Newtonsoft` package. To keep using it, add the package,
import `Marten.Newtonsoft`, and call `opts.UseNewtonsoftForSerialization()`. See
[JSON Serialization](configuration/json.md).

#### Runtime code generation removed {#runtime-code-generation-removed}

Marten no longer compiles C# at runtime and no longer depends on `JasperFx.RuntimeCompiler`. Each place
that used generated code now has a fixed implementation:

| Area | Marten 8 | Marten 9 |
| --- | --- | --- |
| Document storage | A generated class per document type and storage style | Hand-written storage classes in `Marten.Internal.ClosedShape` ([#4404](https://github.com/JasperFx/marten/issues/4404)) |
| Event appends | Generated `GeneratedEventDocumentStorage` | Hand-written storage per append mode ([#4410](https://github.com/JasperFx/marten/issues/4410)) |
| Event reads | Generated selector | Per-column read delegates ([#4411](https://github.com/JasperFx/marten/issues/4411)) |
| Compiled queries | Generated on first use, or ahead of time with `codegen write` | `Marten.SourceGenerator` at build time, with a reflection-based fallback ([#4405](https://github.com/JasperFx/marten/issues/4405)) |
| `AddMartenStore<T>()` | Generated store class | `System.Reflection.Emit` proxy |

What you need to change:

* **Drop the Marten part of `codegen write`.** Delete any pre-generated `Internal/Generated/` folders and
  their `.gitignore` entries. Wolverine and other JasperFx tools still generate code, so keep the step if
  they need it.
* **Remove the code generation settings.** `StoreOptions.GeneratedCodeMode`,
  `SourceCodeWritingEnabled`, `GeneratedCodeOutputPath` and `AllowRuntimeCodeGeneration` no longer exist.
  That includes `x.Production.GeneratedCodeMode` in `CritterStackDefaults()`; the `ResourceAutoCreate`
  settings there are unchanged. `StoreOptions.ApplicationAssembly` stays, since assembly scanning uses it.
* **Update links to the pre-building page.** `/configuration/prebuilding` no longer exists. For
  ahead-of-time work, use [source-generated compiled queries](#source-gen-compiled-queries).
* **Expect mapping errors on first use.** Document mappings are now built the first time a session uses
  that document type rather than at startup, which makes startup faster for applications with many
  document types. The downside is that a bad mapping (a misplaced `[Identity]`, conflicting metadata
  columns) is reported by the first session that touches it instead of by `IHost.StartAsync()`. To keep
  failing at startup, call `store.Storage.BuildAllMappings()` during boot or cover every document type in
  an integration test ([#4303](https://github.com/JasperFx/marten/issues/4303)).

##### Event storage {#closed-shape-event-storage}

The hand-written event storage is the only event store write path. Its operations are built once per
`EventGraph` when the `DocumentStore` is created, so the append mode is fixed at startup. The
`MARTEN_USE_CLOSED_SHAPE_STORAGE` environment variable from the 9.0 alphas is gone. Contributors adding
metadata columns implement `IEventMetadataBinder`; the design is described in
[`src/Marten/EventStorage/README.md`](https://github.com/JasperFx/marten/tree/master/src/Marten/EventStorage).

#### Projections and aggregation

##### Lambda-based projection registration removed {#inline-lambda-projection-removal}

The methods that registered projection handlers as lambdas are gone
([jasperfx#286](https://github.com/JasperFx/jasperfx/issues/286)). The source generator can only find
handlers declared as methods, and Marten 9 has no runtime fallback.

| Removed | Use instead |
| --- | --- |
| `SingleStreamProjection<T, TId>.ProjectEvent<TEvent>(...)` | An `Apply` or `Evolve` method |
| `SingleStreamProjection<T, TId>.CreateEvent<TEvent>(...)` | A `Create` method |
| `SingleStreamProjection<T, TId>.DeleteEvent<TEvent>(...)` overloads that take a lambda | A `ShouldDelete` method, or `Evolve` returning `null` |
| `EventProjection.Project<TEvent>(...)` | A `Project` method |
| `EventProjection.ProjectAsync<TEvent>(...)` | A `ProjectAsync` method |

The parameterless `DeleteEvent<TEvent>()` is still supported.

Move each lambda body into a method on the projection class, and mark the class `partial`:

```csharp
// Marten 8
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

// Marten 9
public partial class OrderProjection : SingleStreamProjection<Order, Guid>
{
    public OrderProjection()
    {
        DeleteEvent<OrderCancelled>();
    }

    public Order Apply(OrderPlaced e, Order order) => order.Apply(e);

    public void Apply(OrderShipped e, Order order) => order.Shipped = e.ShippedAt;

    public bool ShouldDelete(OrderArchived e, Order order) => order.Status == "Closed";
}
```

A delete decision that needs the database can be an async `ShouldDelete` that takes an `IQuerySession`:

```csharp
public async Task<bool> ShouldDelete(Breakdown e, Trip trip, IQuerySession session)
{
    var anyRepairShopsInState = await session.Query<RepairShop>()
        .Where(x => x.State == trip.State)
        .AnyAsync();

    return !anyRepairShopsInState;
}
```

`partial` is needed on projection classes (subclasses of `SingleStreamProjection`,
`MultiStreamProjection` or `EventProjection`) so the generator can add the dispatcher to them.
Self-aggregating types, used through `Projections.Snapshot<T>()`, `LiveStreamAggregation<T>()`,
`AggregateStreamAsync<T>()` or `FetchLatest<T>()`, do not need to be `partial`.

::: warning The source generator must run
A projection that uses `Apply`, `Create` or `ShouldDelete` methods needs its generated dispatcher, unless
it overrides `Evolve`, `EvolveAsync`, `DetermineAction` or `DetermineActionAsync` itself. Without one,
`DocumentStore.For()` throws `InvalidProjectionException: No source-generated dispatcher found`.

The generator ships inside the `Marten` package as an analyzer
([#4557](https://github.com/JasperFx/marten/issues/4557)), so a normal package reference is enough. It
has to run in the assembly that defines the aggregate type. It will not run if your reference to Marten
excludes `analyzers` through `IncludeAssets` or `ExcludeAssets`.
:::

##### Handler methods must be public {#aggregation-public-handlers}

Marten 8 found handler methods by reflection, including private, internal and protected ones. The
generated dispatcher calls your methods directly, so it can only call public ones. **Every `Apply`,
`Create` and `ShouldDelete` method, and every constructor Marten calls, must be public.**

A non-public handler does not cause an error. It is skipped without warning.

| Member | Marten 8 | Marten 9 |
| --- | --- | --- |
| `private void Apply(SomeEvent e)` | Called | Skipped |
| `internal bool ShouldDelete(SomeEvent e)` | Called | Skipped |
| `private SomeAggregate(SomeEvent e)` | Called as `Create` | Skipped |
| `private SomeAggregate()` | Called | The object is created without running a constructor, so field initializers do not run |

```csharp
// Marten 8
public sealed class Invoice : AggregateBase
{
    private Invoice() { }
    private void Apply(InvoiceCreated e) { /* ... */ }
    private void Apply(LineItemAdded e)  { /* ... */ }
}

// Marten 9
public sealed class Invoice : AggregateBase
{
    public Invoice() { }
    public void Apply(InvoiceCreated e) { /* ... */ }
    public void Apply(LineItemAdded e)  { /* ... */ }
}
```

This covers `Apply`, `Create` and `ShouldDelete` on self-aggregating types and on
`SingleStreamProjection` and `MultiStreamProjection` subclasses, `Project` and `ProjectAsync` on
`EventProjection` subclasses, and constructors that take an event. Properties can still have private
setters; only the members Marten calls need to be public.

##### How the event parameter is found {#event-parameter-naming}

When converting lambdas to methods, name the event parameter whatever you like. Marten picks it by type:

* A parameter of type `IEvent<T>` is the event, with event type `T`. Use this form when you need
  [event metadata](/events/metadata).
* Otherwise, the event is the one concrete parameter that is not the aggregate, `IEvent`, a
  `CancellationToken`, or a session interface such as `IQuerySession` or `IDocumentOperations`.

The name only matters when a signature has more than one candidate. Then Marten looks for a parameter
called `@event`, `event`, `e` or `ev`. See
[How Marten Identifies the Event Argument](/events/projections/conventions#how-marten-identifies-the-event-argument).

##### Fewer registration-time errors {#aggregation-validation-rules}

Marten 8 rejected some invalid projections when they were registered. Marten 9 does not, so check for
these yourself:

* **Methods with other names** on a projection class are ignored instead of throwing
  `InvalidProjectionException`. Rename them, or mark them `[JasperFxIgnore]`.
* **An `Apply` on a projection class without an aggregate parameter** is called, but cannot change the
  aggregate. Add the aggregate parameter.
* **A `SingleStreamProjection` whose document type is soft-deleted** is no longer rejected by
  `ValidateConfiguration`.

##### Composite projections have a single shard name

A composite projection now reports one shard name, `<projection-name>/all/v<version>` (for example
`trips/all/v2`), instead of one per member projection
([#4440](https://github.com/JasperFx/marten/issues/4440)). Its members run in sequence against shared
state, so the whole composite has to run on one node; separate shard names let HotCold distribution
split it across nodes.

Code that builds agents or tasks from `ShardNames`, such as Wolverine's `EventSubscriptionAgentFamily`,
now sees one entry per composite, and tests that expected one shard per member should expect `1`. To
list the member projections, use `CompositeProjection.AllProjections()`. There is no switch to restore
the old behavior.

##### `IAggregateGrouper<T>.Group` takes `IReadOnlyList<IEvent>`

The `events` parameter changed from `IEnumerable<IEvent>` to `IReadOnlyList<IEvent>`, so implementations
can count, index and iterate it more than once without copying it
([jasperfx#201](https://github.com/JasperFx/jasperfx/issues/201)). Change the parameter type and remove
any `ToList()` you added to make that safe. The same applies to the lambda overload of
`CustomGrouping()`, although lambdas usually compile unchanged.

##### `IInlineProjection.ApplyAsync` takes `IEnumerable<StreamAction>`

The `streams` parameter changed from `IReadOnlyList<StreamAction>` to `IEnumerable<StreamAction>`, so
Marten no longer builds a list on every `SaveChangesAsync()`
([#4306](https://github.com/JasperFx/marten/issues/4306)). Update custom implementations. If you need
`Count` or indexing, materialize once:

```csharp
public Task ApplyAsync(IDocumentSession session, IEnumerable<StreamAction> streams, CancellationToken ct)
{
    var batch = streams as IReadOnlyCollection<StreamAction> ?? streams.ToList();
    // ...
}
```

#### Synchronous data access removed {#synchronous-data-access-removed}

Marten 9 only supports asynchronous database access
([#4420](https://github.com/JasperFx/marten/issues/4420)). The synchronous methods, which have been
marked `[Obsolete]` since Marten 7, are either gone or throw:

```text
NotSupportedException: As of Marten 9.0, only asynchronous data access is supported
```

The LINQ operators still compile, so a missed one only shows up at runtime. Replace them with their
async versions:

| Marten 8 | Marten 9 |
| --- | --- |
| `session.Query<Foo>().ToList()` or `.ToArray()` | `await session.Query<Foo>().ToListAsync()` |
| `session.Query<Foo>().First()` / `FirstOrDefault()` | `await session.Query<Foo>().FirstAsync()` / `FirstOrDefaultAsync()` |
| `session.Query<Foo>().Single()` / `SingleOrDefault()` | `await session.Query<Foo>().SingleAsync()` / `SingleOrDefaultAsync()` |
| `session.Query<Foo>().Count()` / `LongCount()` | `await session.Query<Foo>().CountAsync()` / `LongCountAsync()` |
| `session.Query<Foo>().Any()` | `await session.Query<Foo>().AnyAsync()` |
| `session.Query<Foo>().Min(x => x.N)`, `Max`, `Sum`, `Average` | `await session.Query<Foo>().MinAsync(x => x.N)`, `MaxAsync`, `SumAsync`, `AverageAsync` |
| `foreach (var f in session.Query<Foo>())` | `await foreach (var f in session.Query<Foo>().ToAsyncEnumerable(ct))` |
| `queryable.ToPagedList(page, size)` | `await queryable.ToPagedListAsync(page, size, ct)` |

There is no `ToArrayAsync()`. Use `ToListAsync()`, and call `.ToArray()` on the result if you need an
array.

The other synchronous members go the same way:

* `Load<T>`, `LoadMany<T>`, `Query<T>(sql, ...)` and the `Json` methods on `IQuerySession` become
  `LoadAsync`, `LoadManyAsync`, `QueryAsync` and the `Json.*Async` methods.
* `PagedList<T>.Create()` and `PagedList<T>.Init()` throw; use `ToPagedListAsync()`.
* `IQueryHandler<T>` no longer has a synchronous `Handle(DbDataReader, IMartenSession)`. Delete your
  override and implement only `HandleAsync`.
* `ExecuteHandler<T>()` throws; use `ExecuteHandlerAsync()`.

If you genuinely have to block, call `.GetAwaiter().GetResult()` on the async method yourself.

#### Document revisions are tracked as `long`

`IRevisioned.Version` is still an `int`, so ordinary revisioned documents need no change. Internally,
Marten now tracks revisions as 64-bit values, and these members widened from `int` to `long`:

| Member | Marten 8 | Marten 9 |
| --- | --- | --- |
| `DocumentMetadata.CurrentRevision` | `int` | `long` |
| `IDocumentSession.UpdateRevision<T>()` revision parameter | `int` | `long` |
| `IDocumentSession.TryUpdateRevision<T>()` revision parameter | `int` | `long` |
| `IRevisionedOperation.Revision` | `int` | `long` |
| `m.Revision` in `MartenRegistry` metadata configuration | `Column<int>` | `Column<long>` |

Calls that pass an `int` still compile. You only need to change:

* a custom `IRevisionedOperation`, whose `Revision` is now `long`;
* a custom `IBulkLoader<T>` for a revisioned document, which must write the expected version as
  `NpgsqlDbType.Bigint` instead of `NpgsqlDbType.Integer`.

A `[Version]` property can be `int` or `long`. The `mt_version` column is migrated to `bigint`
automatically (see [Schema changes](#schema-changes)).

::: tip If you upgraded from an early 9.0 alpha
An early alpha changed `IRevisioned.Version` to `long`. That was reverted before the release candidate
([#4533](https://github.com/JasperFx/marten/pull/4533)). If you changed your documents to a `long`
version for that alpha, implement [`ILongVersioned`](#ilongversioned) instead.
:::

#### Moved, renamed and removed APIs {#schema-dedup-audit-relocations}

**Moved to Weasel.** Only the namespace changes:

* `Marten.Internal.Operations.OperationRole` is now `Weasel.Core.OperationRole`
  ([#4350](https://github.com/JasperFx/marten/issues/4350)).
* `Marten.BulkInsertMode` is now `Weasel.Core.BulkInsertMode`
  ([weasel#264](https://github.com/JasperFx/weasel/issues/264)).

**`IStorageOperation` lost its synchronous `Postprocess()`.** It now extends
`Weasel.Core.IStorageOperation`. Custom implementations must move that logic into `PostprocessAsync()`
([#4351](https://github.com/JasperFx/marten/issues/4351)).

**Renamed:**

| Marten 8 | Marten 9 |
| --- | --- |
| `ProjectionBase.ProjectionName` | `Name` |
| `ProjectionBase.ProjectionVersion` | `Version` |
| `JasperFxSubscriptionBase.SubscriptionName` | `Name` |
| `JasperFxSubscriptionBase.SubscriptionVersion` | `Version` |
| `EventSlice<T>.Aggregate` and `IEventSlice<T>.Aggregate` | `Snapshot` |
| `MessageMetadata.LastModifiedBy` and `IMetadataContext.LastModifiedBy` | `CurrentUserName` |
| `OaktonEnvironment`, `ApplyOaktonExtensions()`, `RunOaktonCommands()` | `JasperFxEnvironment`, `ApplyJasperFxExtensions()`, `RunJasperFxCommands()` |

The `LastModifiedBy` rename only affects the session's current user name. The document metadata
properties `IDocumentSession.LastModifiedBy` and `DocumentMetadata.LastModifiedBy` keep their names.

**Removed:**

* `IEventStore.TeardownExistingProjectionProgressAsync()`. Use `TeardownExistingProjectionStateAsync()`,
  which has the same signature. The old method always removed the projected documents too, so the
  behavior is the same.
* `MultiStreamProjection.CustomGrouping(IEventSlicer<...>)`. Pass a grouping lambda or an
  `IAggregateGrouper<TId>` instead.
* Every other member that was `[Obsolete]` in Marten 8. Clear your obsolete warnings on Marten 8 before
  upgrading.

`CombGuidIdGeneration` is still `[Obsolete]` but has not been removed yet.

### New features {#new-features-in-9-0-0}

#### Source-generated compiled queries {#source-gen-compiled-queries}

`Marten.SourceGenerator` builds the handlers for compiled queries at compile time. Add the package to the
project that declares your `ICompiledQuery<,>` types:

```xml
<PackageReference Include="Marten.SourceGenerator" PrivateAssets="all" />
```

and mark that assembly:

```csharp
[assembly: JasperFx.JasperFxAssembly]
```

Generated handlers read query parameters directly instead of through reflection, which is about 31%
faster per call, and they work with AOT publishing.

Queries the generator cannot handle still work, through a reflection-based handler built once on first
use ([#4405](https://github.com/JasperFx/marten/issues/4405)). That covers:

* queries whose SQL needs an `ICompiledQueryAwareFilter`: string `Contains`, `StartsWith` and `EndsWith`,
  `HashSet<T>.Contains`, `Dictionary<,>.ContainsKey`, and counts over child collections;
* generic or nested compiled query types;
* compiled queries in an assembly without `[JasperFxAssembly]`.

#### `ILongVersioned` for 64-bit document versions {#ilongversioned}

`ILongVersioned` is the `long` counterpart of `IRevisioned`
([#4526](https://github.com/JasperFx/marten/issues/4526)). Use it when a document's version is the
global event sequence number, as it is for documents built by a `MultiStreamProjection`. That number can
exceed `Int32.MaxValue`, at which point an `IRevisioned` document fails to load.

```csharp
public class CustomerSummary : ILongVersioned
{
    public Guid Id { get; set; }
    public long Version { get; set; }
}
```

Both interfaces turn on numeric revisions and use the same `bigint` column.

#### HSTORE storage for DCB tags

`DcbStorageMode.HStore` stores [DCB](events/dcb.md) tags in a single `hstore` column on `mt_events` with
one GIN index, instead of one table per tag type
([#4238](https://github.com/JasperFx/marten/issues/4238)). The default is still
`DcbStorageMode.TagTables`, so nothing changes unless you opt in:

```csharp
opts.Events.DcbStorageMode = DcbStorageMode.HStore;
opts.Events.RegisterTagType<StudentId>("student");
```

HStore is a good fit when your DCB queries usually match two or more tag types (about 90% faster for
`QueryByTagsAsync` and 70% faster for `EventsExistAsync` with two tags), when `FetchForWritingByTags` is
on a hot path (about half the round-trip time), or when the number of tag tables is getting hard to
manage.

Stay on `TagTables` if most of your queries check a single tag with `EventsExistAsync`, where HStore is
slightly slower, or if you cannot install the `hstore` extension. The mode is chosen when the database
is created, and there is no migration between modes, so an existing store stays on `TagTables`. See
[Choosing a Storage Mode](events/dcb.md#choosing-a-storage-mode) for the full comparison.

#### `[Identity]` on members not named `Id` {#aggregation-identity-attribute}

The source generator recognizes `[Identity]` on an aggregate member with any name:

```csharp
public record LoadTestInlineProjection
{
    [Identity]
    public string StreamKey { get; init; }

    public LoadTestInlineProjection Apply(LoadTestEvent e, LoadTestInlineProjection current) => /* ... */;
}
```

An identity configured at runtime with `opts.Schema.For<T>().Identity(x => x.SomeMember)` is invisible to
the generator. Use the attribute, or name the member `Id`.

#### Aggregates with `required` members {#aggregation-required-members}

An aggregate type can declare `required` members. The generator creates the first instance with those
members set to `default!` and then calls your first `Apply`, which is expected to set them:

```csharp
public class ExternalAccountLink
{
    public required string Id { get; set; }
    public required Guid CustomerId { get; set; }
}

public partial class ExternalAccountLinkProjection : SingleStreamProjection<ExternalAccountLink, string>
{
    public void Apply(CustomerLinkedToExternalAccount e, ExternalAccountLink link)
    {
        link.Id = e.ExternalAccountId;
        link.CustomerId = e.CustomerId;
    }
}
```

To avoid the `default!` placeholders, add a `public static T Create(SomeEvent e)` method and the first
event goes through it instead.

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
