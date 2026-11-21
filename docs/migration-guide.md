# Migration Guide

## Key Changes in 10.0.0

### Platform and packages {#platform-and-packages-in-10-0-0}

**.NET 9 is no longer supported.** Marten 10 targets `net10.0` and `net11.0`. Stay on Marten 9 if you need
`net9.0`.

**The shared Critter Stack packages moved up a major version together:**

| Package | Marten 9 | Marten 10 |
| --- | --- | --- |
| `JasperFx`, `JasperFx.Events` | 2.x | 3.x |
| `Weasel.Postgresql`, `Weasel.Storage` | 9.x | 10.x |
| `Npgsql` (and its plugin packages) | 9.x | 10.x |
| `Npgsql.EntityFrameworkCore.PostgreSQL` (for `Marten.EntityFrameworkCore`) | 9.0.x / 10.0.x per TFM | 10.0.x |

You get these transitively from `Marten`. If you reference any of them directly, upgrade them at the same time.
On `net11.0`, `Microsoft.Extensions.*` packages must also be on 11.x, otherwise NuGet reports NU1605.

### JasperFx 3.9.0: names no longer depend on the machine's culture <Badge type="warning" text="behavior change" />

Marten's default event type names, the aggregate type names on `mt_streams`, DCB tag table suffixes, duplicated-field
column names and computed index names are now lowercased the same way in every culture. If you ran Marten on a
Turkish or Azerbaijani machine with an `I` in one of those type or member names, map the old event type names with
an alias before upgrading ([details](https://shared-libs.jasperfx.net/migration-guide#key-changes-in-3-9-0)).
Duplicated-field columns, tag tables and indexes created under the old names are created again under the new names
by the next schema migration, and the data in the old ones is not copied over, so rebuild or copy that data.
Separately, `Evolve()` methods may now use bare `case Foo:` labels, and the source generator bundled in the Marten
package picks them up.

### Async projections park a failing identity instead of skipping the event {#async-projections-park-a-failing-identity} <Badge type="warning" text="behavior change" />

Marten 10 takes JasperFx 3.10's parking ([details](https://shared-libs.jasperfx.net/events/parked-identities)) and
makes it the default for async projections. `opts.Projections.Errors.ApplyErrors` and `SerializationErrors` are now
`ProjectionErrorAction.Park`; in Marten 9 both errors were skipped and dead-lettered.

- A single-stream projection, and a multi-stream projection grouped with `IdentityFromStream<TEvent>()` or
  `IdentityFromTag<TTag>()` or kept with `UseSliceIndex()`, parks the identity that failed. Its document stays at the
  last good state, its later events are held back, and everything else keeps going.
- Any other projection **pauses** instead of skipping: a multi-stream projection grouped with `Identity()`,
  `Identities()` or custom grouping, an event projection, a flat table projection and a composite projection. This is
  the part most likely to surprise you, so check the log line Marten writes for each async projection at startup.
- Rebuilds still pause on an error, and never park.

The schema gains `mt_parked_identities` (always created), a nullable `parked_count` column on `mt_event_progression`
(written with the extended progression heartbeat), and one `mt_slice_index_*` table for each projection version that
calls `UseSliceIndex()`. Under `AutoCreate.None`, apply the migration before deploying. `opts.RestoreV9Defaults()`
puts skipping back, but doesn't remove the table. See [parking a failing identity](/events/projections/async-daemon#parking-a-failing-identity).

### Runtime-async on `net11.0` <Badge type="warning" text="behavior change" />

The `net11.0` build of Marten is compiled with the C# compiler's runtime-async feature, so its async methods run as
.NET 11 runtime-async methods instead of compiler-built state machines. That allocates less per call, but stack
traces, debugger stepping and any APM agent or profiler that hooks async state machines may look or behave
differently. The `net10.0` build is unchanged.

To build Marten from source without it, pass `-p:MartenRuntimeAsync=false`. Code that Marten's source generators
write into your project is compiled by your project, so your own compiler settings decide there
([#5886](https://github.com/JasperFx/marten/issues/5886)).

### Synchronous data access is gone {#synchronous-data-access-is-gone-in-10-0-0}

::: warning Breaking change
This is the change most applications hit first when they move to Marten 10. It compiles cleanly where LINQ is
concerned, so a missed call only shows up at runtime.
:::

Marten 9 made the synchronous data-access methods throw ([Synchronous data access removed](#synchronous-data-access-removed)).
Marten 10 finishes the job ([#5861](https://github.com/JasperFx/marten/issues/5861)): the synchronous session members
that were left are deleted, and every synchronous LINQ operator on a Marten query throws a
`NotSupportedException` that names the asynchronous operator to use.

#### Synchronous LINQ operators throw

Calling a synchronous LINQ operator on a Marten query (`ToList()`, `ToArray()`, `ToDictionary()`, `First()`,
`Single()`, `Count()`, `Any()`, `Sum()`, `foreach` and the rest) still compiles, because the operators come from
.NET's `System.Linq`, but it throws when it runs. Before:

```cs
var users = session.Query<User>().Where(x => x.Active).ToList();
var first = session.Query<User>().First(x => x.UserName == "jdm");
var count = session.Query<User>().Count();

foreach (var user in session.Query<User>())
{
    Console.WriteLine(user.UserName);
}
```

After:

```cs
var users = await session.Query<User>().Where(x => x.Active).ToListAsync();
var first = await session.Query<User>().FirstAsync(x => x.UserName == "jdm");
var count = await session.Query<User>().CountAsync();

await foreach (var user in session.Query<User>().ToAsyncEnumerable())
{
    Console.WriteLine(user.UserName);
}
```

The exception names the operator you called and the one to use instead. For a single-result or aggregating operator:

```text
System.NotSupportedException: Synchronous data access is not supported in Marten 10: First() would query the database synchronously. Use await FirstAsync() instead.
```

For anything that enumerates the query (`ToList()`, `ToArray()`, `ToDictionary()`, `ToHashSet()`, `foreach`):

```text
System.NotSupportedException: Synchronous data access is not supported in Marten 10: enumerating a Marten query (foreach, ToList(), ToArray(), ToDictionary(), ToHashSet() and the like) would query the database synchronously. Use await ToListAsync() instead, or await foreach over ToAsyncEnumerable().
```

`ToPagedList()` names `ToPagedListAsync()` the same way, and an operator with no direct asynchronous counterpart
(`Aggregate()`, say) lists the ones that exist. The replacements:

| Synchronous | Marten 10 |
| --- | --- |
| `ToList()`, `ToArray()`, `ToDictionary()`, `ToHashSet()` | `await ToListAsync()`, then convert the list |
| `foreach (var x in query)` | `await foreach (var x in query.ToAsyncEnumerable())` |
| `First()`, `FirstOrDefault()`, `Single()`, `SingleOrDefault()` | `await FirstAsync()`, `FirstOrDefaultAsync()`, `SingleAsync()`, `SingleOrDefaultAsync()` |
| `Count()`, `LongCount()`, `Any()` | `await CountAsync()`, `LongCountAsync()`, `AnyAsync()` |
| `Sum()`, `Min()`, `Max()`, `Average()` | `await SumAsync()`, `MinAsync()`, `MaxAsync()`, `AverageAsync()` |
| `All(predicate)` | `!await AnyAsync(...)` with the predicate negated |
| `Last()`, `LastOrDefault()` | reverse the ordering, then `await FirstAsync()` / `FirstOrDefaultAsync()` |
| `ToPagedList(page, size)` | `await ToPagedListAsync(page, size)` |

Composing a query is untouched. `Where()`, `OrderBy()`, `Select()`, sub-queries inside a predicate
(`x.Children.Any(...)`), `Include()`, compiled queries and `ToCommand()` build an expression and never execute it;
only the operator at the end decides whether the database is queried.

#### Removed session members

| Removed | Use instead |
| --- | --- |
| `IQuerySession.Connection` | `await session.GetConnectionAsync(token)` |
| `IDocumentSession.BeginTransaction()` | `await session.BeginTransactionAsync(token)` |
| `IDocumentStore.LightweightSession(IsolationLevel.Serializable)`, and the same for `IdentitySession`, `DirtyTrackedSession`, `OpenSession(SessionOptions)` and `QuerySession(SessionOptions)` with `IsolationLevel.Serializable` | `await store.LightweightSerializableSessionAsync()`, `IdentitySerializableSessionAsync()`, `DirtyTrackedSerializableSessionAsync()`, `OpenSerializableSessionAsync(options)` or `QuerySerializableSessionAsync()` |

`GetConnectionAsync()` behaves as reading `Connection` did. It opens the session's connection if need be and, on a
session that otherwise opens and closes a connection per command, pins the session to one connection and begins its
transaction, so your own commands run in the session's transaction. The session still owns the connection: do not
dispose it. Before:

```cs
var count = await session.Connection
    .CreateCommand("select count(*) from tasks where status = 'open'")
    .ExecuteScalarAsync();

session.BeginTransaction();
```

After:

```cs
var connection = await session.GetConnectionAsync();
var count = await connection
    .CreateCommand("select count(*) from tasks where status = 'open'")
    .ExecuteScalarAsync();

await session.BeginTransactionAsync(CancellationToken.None);
```

The synchronous session factories keep their `IsolationLevel` parameter, and `ReadCommitted` (the default),
`RepeatableRead` and the other levels still work. Only `Serializable` is refused, because a Serializable session has
to open its connection and begin its transaction as it is created:

```text
System.NotSupportedException: Synchronous data access is not supported in Marten 10, and a Serializable session has to open its connection and begin its transaction when it is created. Open it with LightweightSerializableSessionAsync(), IdentitySerializableSessionAsync(), DirtyTrackedSerializableSessionAsync(), QuerySerializableSessionAsync() or OpenSerializableSessionAsync() instead.
```

A custom `ISessionFactory` that returned `LightweightSession(IsolationLevel.Serializable)` has to change too, since
`ISessionFactory.OpenSession()` is synchronous.

#### Sessions are still `IDisposable`, but prefer `await using`

Sessions keep `Dispose()`, so `using var session = store.LightweightSession();` still compiles and works. With
[row level security](/documents/multi-tenancy#row-level-security-) on, a synchronous `Dispose()` makes one synchronous
database call: it resets the tenant setting on a connection the session opened, before releasing that connection to
the pool. This is the only synchronous database call left in Marten 10. It is there so a stale tenant cannot outlive
the session on a pooled connection when the connection string sets `No Reset On Close=true`. If the reset fails,
`Dispose()` does not throw. Marten logs the failure and makes sure the connection is not returned to the pool as
healthy.

A connection you supplied (`SessionOptions.ForTransaction()`) is yours, so a synchronous `Dispose()` leaves its tenant
setting alone. `DisposeAsync()` resets it. Either way, `await using` is still the recommended way to dispose a
session.

#### Lower-level changes

These only matter if you implement or call Marten's connection plumbing directly:

- `IConnectionLifetime` no longer has `Execute`, `ExecuteReader(NpgsqlCommand)` or `ExecuteReader(NpgsqlBatch)`.
- `IAlwaysConnectedLifetime` loses `Connection` and `BeginTransaction()` and gains `OpenConnectionAsync(CancellationToken)`.
  `ITransactionStarter` loses `Start()`; use `StartAsync(CancellationToken)`.
- `QuerySession.ExecuteReader(NpgsqlBatch)` is deleted; use `ExecuteReaderAsync`.
- `IDiagnostics.GetPostgresVersion()` is replaced by `GetPostgresVersionAsync(CancellationToken)`.
- `QuerySession.SynchronousNotSupportedMessage` now reads
  `Synchronous data access is not supported in Marten 10. Use the asynchronous equivalent.`

### HiLo blocks are reserved one block ahead

HiLo ids are still assigned synchronously in `session.Store()`, but each sequence now reserves its **next** block in the
background with an asynchronous database call as soon as it starts using the current one (#5862). Exhausting a block normally
no longer blocks the storing thread on the database. When the next block has not arrived yet, `Store()` fetches one synchronously
straight away, as it always did, rather than waiting on the background fetch; waiting on asynchronous work from a synchronous
`Store()` is what let a burst of storing threads starve the thread pool
([#5969](https://github.com/JasperFx/marten/issues/5969)). The first id a database issues builds or updates `mt_hilo` and
`mt_get_next_hi` with synchronous database calls instead of blocking on Weasel's asynchronous migration, and with
`AutoCreate.None` it no longer reads the catalog at all.

Each process can therefore hold one unused block, so expect gaps of up to `MaxLo` ids when a process stops, and ids are no
longer guaranteed to be contiguous across a block boundary. They are still unique and, within one process, increasing.
Nothing needs to change in your code. See [How HiLo blocks are fetched](/documents/identity#how-hilo-blocks-are-fetched).

The `mt_get_next_hi` function behind HiLo is now a single atomic increment ([#5938](https://github.com/JasperFx/marten/issues/5938)).
The old version read the current value and then updated it only if it was unchanged, returning `-1` to every caller that another
caller overtook. Marten retried those calls with no backoff, and under heavy concurrent load a sequence could exhaust
`MaxAdvanceToNextHiAttempts` and throw `HiloSequenceAdvanceToNextHiAttemptsExceededException`. The new version makes overlapping
callers wait on the row lock instead, and each gets a block. It is an ordinary schema change, applied with the rest of your
schema migration; until it is applied, the old function keeps working as it did.

### Abstractions lifted into JasperFx and Weasel <Badge type="warning" text="recompile" />

Marten 10 shares more of its machinery with Polecat and Fisher. The behavior is the same unless listed here, but
assemblies compiled against Marten 9 must be recompiled.

- `Marten.ITransactionParticipant` is now `ITransactionParticipant<NpgsqlConnection, NpgsqlTransaction>` from
  `Weasel.Storage`. `BeforeCommitAsync` and `AfterCommitAsync` keep their signatures, so an implementer's source is
  unchanged.
- `Marten.ISerializer` also extends `Weasel.Core.ISerializer`, the serializer contract Polecat and Fisher share, so
  a Marten serializer can be passed to code written against it. It gains `FromJson<T>(string)` and
  `FromJson(Type, string)`, default-implemented over the stream overloads. A custom serializer's source is unchanged.
- `OpenTelemetryOptions` derives from JasperFx's `OpenTelemetryOptions<IChangeSet>`, and Marten uses the process-wide
  `Marten` `ActivitySource`, so a listener on `Marten` hears one source rather than two.
- `MartenActivator` derives from JasperFx's `StoreActivator`. Behavior changes: databases are built only when apply or
  assert is opted in (Marten's activator still builds them at startup for master-table tenancy); under
  `ContinueOnFailures`, a database that failed to migrate skips its seeders and rolling-partition pass; a cancelled
  startup is no longer logged as a failed migration; each seeder checks the startup token before it runs.
- `MasterTableTenancy` derives from Weasel's `MasterTableTenancyBase<MartenDatabase, NpgsqlDataSource>`, the
  control-table runtime Polecat and Fisher share. The control table, its SQL and the exceptions are unchanged.
  Behavior changes:
  - Refreshing the tenant databases (`BuildDatabases()`, which the async daemon calls to discover tenants) now evicts
    a cached tenant whose row was deleted or disabled elsewhere. It used to only add, so such a tenant kept resolving
    until the process restarted. A tenant whose connection string changed gets a new database.
  - `DeleteDatabaseRecordAsync` and `ClearAllDatabaseRecordsAsync` evict the tenants they remove from the cache.
  - `AddDatabaseRecordAsync` caches the tenant's database straight away, as `AddTenantAsync` already did.
  - `FindOrCreateDatabase` and `TryFindDatabase` cache what they resolve rather than building a new database, and a
    new data source, on every call.
  - Any lookup, not only `BuildDatabases()`, applies the control table's migration first when `AutoCreate` allows it.
  - A data source supplied through `MasterTableTenancyOptions.DataSource` is no longer disposed with the store.
  - Seeded tenant ids go through `TenantIdStyle` like every other tenant id.
  - The lifecycle methods (`AddDatabaseRecordAsync`, `DeleteDatabaseRecordAsync`, `DisableTenantAsync`, `EnableTenantAsync`,
    `AllDisabledAsync`, `ClearAllDatabaseRecordsAsync`) take an optional `CancellationToken`.
- Document storage is Weasel's closed-shape storage family (`Weasel.Storage`), the one Polecat and Fisher share:
  `QueryOnlyClosedShapeDocumentStorage`, `LightweightClosedShapeDocumentStorage`,
  `IdentityMapClosedShapeDocumentStorage`, `DirtyTrackingClosedShapeDocumentStorage` and
  `ClosedShapeSubClassDocumentStorage`, each carrying Marten's rules. The SQL and the write operations are unchanged.
  `Marten.Internal.Storage.DocumentStorage<T, TId>` and its `LightweightDocumentStorage`, `IdentityMapDocumentStorage`,
  `DirtyCheckedDocumentStorage` and `QueryOnlyDocumentStorage` subclasses are deleted, and the
  `Marten.Internal.ClosedShape` storage classes are internal; use `IDocumentStorage<T, TId>`. Behavior changes:
  - Storing a second instance under an id already in the identity map still throws `InvalidOperationException`, with
    a message that says how to get out of it.
  - An identity map holding a different id type is refused with Weasel's `IdentityMapIdTypeMismatchException`. The
    session's own id-type check, which is what a wrong-typed `LoadAsync` hits, still throws
    `DocumentIdTypeMismatchException`.
  - A sub-class storage reports its root's `UseNumericRevisions` and `HardDeleteFragment`, which used to be `false` and
    `null`.
- A document stored twice in one unit of work by a dirty-tracking session now gets one tracker, not two.
- `Marten.Services.VersionTracker` (unused) and `Marten.Events.Dcb.TagValueStringifier` (now in `JasperFx.Events.Tags`)
  are deleted.
- The `IReadOnlyEventStoreOptions` properties lost their setters (`AppendMode`, `UseArchivedStreamPartitioning`,
  `UseTenantPartitionedEvents`, `MessageOutbox` and so on). Set them on `StoreOptions.Events` as before.

### `Store()` checks a numerically revisioned document's revision <Badge type="warning" text="breaking" />

`IDocumentSession.Store()` on a document using numeric revisions (`IRevisioned`, `ILongVersioned`, a `[Version]` member
or `Metadata.Revision.MapTo(...)`) now honours the revision the document carries, as the
[concurrency docs](/documents/concurrency#numeric-revisioned-documents) always said it did and as Polecat and Fisher
already do ([#5788](https://github.com/JasperFx/marten/issues/5788)):

- A **non-zero** revision is the revision the document is asked to *become*. It is accepted only when strictly greater
  than the stored revision, and the row lands at exactly that value. Otherwise `SaveChangesAsync()` throws
  `ConcurrencyException`.
- A revision of **0** is the "auto" sentinel and behaves as before: no check, the next revision is assigned.

Marten 9 ignored the document's revision on `Store()` and always took the auto path, so a stale writer silently
overwrote a newer revision. The visible change is in ordinary read-modify-write code: **re-storing a loaded
`IRevisioned` document now throws `ConcurrencyException`**, because after a load or a save the document carries the
revision that is already stored, and that is not greater than itself. Either of these restores a working write:

```cs
var doc = await session.LoadAsync<Reservation>(id);
doc.Name = "changed";

// Name the revision the document becomes -- the optimistic check still applies
session.UpdateRevision(doc, doc.Version + 1);

// ...or opt out of the check and let Marten assign the next revision
doc.Version = 0;
session.Store(doc);
```

Sessions opened with `ConcurrencyChecks.Disabled` still overwrite without a check. Also fixed along the way: in a
dirty-tracking session, a loaded document that was changed and then passed to `UpdateRevision()`/`TryUpdateRevision()`
lost its explicit revision to the automatically detected change, so the revision check never ran.

### Private aggregate constructors run again {#private-aggregate-constructors-run-again}

::: warning Behavior change
This changes what happens when an aggregate is created, compared with Marten 9 and the earlier 10.0 alphas.
:::

When the source-generated dispatcher has to create an aggregate and the type's only parameterless constructor is
`private`, `protected` or `internal`, it now calls that constructor through a generated `[UnsafeAccessor]`
([jasperfx#1188](https://github.com/JasperFx/jasperfx/issues/1188), JasperFx 3.3.0,
[#5915](https://github.com/JasperFx/marten/issues/5915)). The constructor body and every field and property
initializer run, which is what Marten 8 did through `Activator.CreateInstance(nonPublic: true)`. The accessor is
bound by the JIT or by Native AOT's compiler, not by reflection, so it is trim and AOT safe.

Marten 9 and the 10.0 alphas created such an aggregate with `RuntimeHelpers.GetUninitializedObject` instead, so
nothing in the constructor ran. Code that worked around that keeps working: an `Apply` that initializes a list with
`??=` finds it already initialized. Check two things when upgrading:

- **A private parameterless constructor with side effects now runs.** If it raises an event, registers the instance
  somewhere, increments a counter or sets a default that `Apply` overwrites, that happens again for every aggregate
  the dispatcher creates, including on every live aggregation and every projection rebuild.
- **Initializers set state before the first `Apply`.** A property initializer such as `Status = Status.Draft` that
  was skipped in Marten 9 is now in place when the first event is applied.

A type with no parameterless constructor at all (a record with a primary constructor, for example), a generic
aggregate type, and an aggregate type nested inside a `private` or `protected` type are still created without
running a constructor. See [Handler methods must be public](#aggregation-public-handlers).

### Binary events are written and read through pooled buffers

`IEventBinarySerializer` gains `Serialize(Type, object, IBufferWriter<byte>)` and
`Deserialize(Type, ReadOnlySpan<byte>)` (JasperFx 3.3.0). Both are default members over the `byte[]` pair, so an
existing serializer keeps compiling and working unchanged. Marten now uses them for `mt_events.bdata`: Quick appends
and `BulkInsertEventsAsync` serialize into pooled buffers, and reads deserialize from a span over a rented buffer,
so a serializer that overrides them (`Marten.MemoryPack` does) no longer costs one array per event on those paths
([#5731](https://github.com/JasperFx/marten/issues/5731)). `Rich` appends still allocate one array per binary
event. See [Pooled buffers](/events/binary-serialization#pooled-buffers).

### C# 15 union types on `net11.0` <Badge type="tip" text="new" />

On `net11.0`, a document member or an event may be a C# union. An appended union is recorded as its active case,
and `Apply(TUnion)` projections see every case. LINQ reaches a union member through `.Value`:
`x.Payment.Value is Card`, `((Card)x.Payment.Value).Last4`, `(x.Payment.Value as Cash).Amount`, and `== null`.
Marten refuses with `BadLinqExpressionException` a type no case is, cases the stored JSON cannot tell apart, a nested
union case, an enum case read, ordering or grouping by the union, a union inside a child collection predicate, the
Newtonsoft serializer, and a patch path through a case. On `net10.0` no type is a union, so none of this applies.

### `UnArchiveStream` <Badge type="tip" text="new" />

`session.Events.UnArchiveStream(Guid)` and `UnArchiveStream(string)` reverse `ArchiveStream()`. The stream and its
events become live again and accept appends, including under `UseArchivedStreamPartitioning`, where the rows (and
their DCB tag and natural key rows) move back out of the archived partitions. This is the operation the
`ArchivedStreamException` message points to. Both overloads are declared on the shared
`JasperFx.Events.IEventStoreOperations` (JasperFx 3.3.0), which Marten's `IEventStoreOperations` inherits. See
[Un-archiving a Stream](/events/archiving.html#un-archiving-a-stream).

The schema gains one function, `mt_unarchive_stream`. With `UseArchivedStreamPartitioning` and natural keys,
`mt_archive_stream` also changes: it now copies the natural key rows into the archived partition instead of letting
the foreign key cascade delete them, so un-archiving can restore them. Both changes apply with your normal schema
migration.

### LINQ `Join()`, `LeftJoin()` and `RightJoin()` translate <Badge type="tip" text="new" />

- `Queryable.Join()`, .NET 10's `LeftJoin()` and `RightJoin()`, and the .NET 11 overloads of the three that return
  `(Outer, Inner)` tuples now translate to SQL joins. Marten rewrites each into the `GroupJoin()` + `SelectMany()`
  (+ `DefaultIfEmpty()`) form it already translated, so the SQL is the same as for that form. `FullJoin()` is still
  refused, as are a few shapes the rewrite cannot answer correctly: an operator other than `Where()` before the join,
  a source that is not `session.Query<T>()`, a composite key, a comparer, and a join of a join. See
  [Joining Documents with GroupJoin](/documents/querying/linq/group-join#join-leftjoin-rightjoin-and-fulljoin).
- Two fixes to the `GroupJoin()` + `SelectMany()` form that the new operators share. `Any()` over a join now asks
  whether the join has a row; it used to answer for the outer side alone, ignoring the join and any filter on the inner
  side. A `Where()` on the inner side after a left join (`DefaultIfEmpty()`) now throws `BadLinqExpressionException`;
  it used to be applied before the join, which kept the outer rows the filter should have removed.

### LINQ `jsonb` filter parameters

- The containment (`@>`) and JSONPath filters LINQ builds for child-collection and dictionary queries bind their
  `jsonb` parameter as UTF-8 `byte[]` rather than a string, as document writes already did. A logger that prints
  `NpgsqlParameter.Value` shows `System.Byte[]` for that parameter; `Encoding.UTF8.GetString` recovers the JSON.

### LINQ `Count()` after `Take()` or `Skip()` counts the page <Badge type="warning" text="behavior change" />

- A correctness fix ([#5920](https://github.com/JasperFx/marten/issues/5920)), so these counts change value.
  `Count()` and `LongCount()` after `Take()` and/or `Skip()` now return the number of rows on that page, as LINQ to
  Objects does. Marten used to put the `LIMIT`/`OFFSET` on the `count(*)` itself, so `Take(n).Count()` returned the
  full count and `Skip(k).Count()` or `Skip(k).Take(n).Count()` returned `0`. The paged query is now counted from a
  CTE. Compiled queries, `SelectMany()` before or after the paging, and `Take(0).Any()` (which returned `true`) are
  fixed too. A count without `Take()` or `Skip()` sends exactly the SQL it did before.

### LINQ `Take()` or `Skip()` before `SelectMany()` pages the parent documents <Badge type="warning" text="behavior change" />

- A correctness fix ([#5928](https://github.com/JasperFx/marten/issues/5928)), so these queries return different
  rows. `OrderBy(x => x.Number).Take(3).SelectMany(x => x.Children)` now returns every child of the first three
  documents, as LINQ to Objects and EF Core do. Marten used to put the `LIMIT`/`OFFSET` on the statement that
  flattens the collection, so it returned the first three children overall. `Skip()`, `Skip().Take()`, value
  collections (`SelectMany(x => x.Numbers)`), and `First()`, `Single()` and `Any()` after such a query follow the
  paged documents too. Paging after the `SelectMany()` still pages the flattened rows, and a `SelectMany()` without
  paging before it sends exactly the SQL it did before. The flattened rows come back in the order of the paged
  documents, and in collection order within each document.

### LINQ indexes into document arrays translate everywhere <Badge type="warning" text="behavior change" />

- [#5949](https://github.com/JasperFx/marten/issues/5949): an index into an array or list on the document,
  `x.Values[1]`, `x.List[1]` or `x.Values.ElementAt(1)`, now translates in `OrderBy()`/`ThenBy()` and `Select()` as
  well as in `Where()`, and jagged indexes such as `x.Grid[1][0]` translate in all three. A `bool[]` or `List<bool>`
  on the document can be queried too. Before, it could not be queried at all. See
  [Reading an Element by Position](/documents/querying/linq/child-collections#reading-an-element-by-position).
- The behavior change: a **negative** index now throws `BadLinqExpressionException`. It used to translate to a
  negative `jsonb` index, which PostgreSQL reads from the end of the array, so `x.Values[-1]` silently compared the
  last element.
- An index past the end of the stored array reads as `null`, as it always has in `Where()`.

### LINQ ordering by a value that reads nothing from the document <Badge type="tip" text="new" />

- [#5950](https://github.com/JasperFx/marten/issues/5950): `OrderBy()`, `OrderByDescending()`, `ThenBy()` and
  `ThenByDescending()` over a static member, a captured variable or a literal used to throw
  `Invalid OrderBy() expression`. Such a key is the same for every document, so it is now dropped from the
  `ORDER BY`, and the remaining keys keep their order, as in LINQ to Objects. The SQL for every other ordering is
  unchanged.

### LINQ: a second `OrderBy()` re-sorts <Badge type="warning" text="behavior change" />

- A correctness fix ([#5959](https://github.com/JasperFx/marten/issues/5959)), so these queries return a different
  order. `OrderBy(x => x.Category).OrderBy(x => x.Name)` now orders by `Name`, then `Category`, as LINQ to Objects
  does: a later `OrderBy()`/`OrderByDescending()` (or `OrderBySql()`, `OrderByTextRank()`, `OrderByNgramRank()`)
  re-sorts, and the earlier keys only break its ties. Marten used to put the later key behind the earlier ones.
  If you relied on that, write the chain with `ThenBy()`. A chain with one `OrderBy()` sends exactly the SQL it did
  before, and so does `OrderBy("a", "b")`, which is now built with `ThenBy()`. `ThenByTextRank()` followed by
  another `ThenBy()` now keeps its place in the chain instead of moving to the end.

### LINQ computed `OrderBy()` keys are computed <Badge type="warning" text="behavior change" />

- A correctness fix ([#5957](https://github.com/JasperFx/marten/issues/5957)), so these queries return a different
  order or throw. A key computed from the document's members, such as `OrderBy(x => -x.Number)`, used to be
  replaced by the member underneath it: that query sent `order by CAST(d.data ->> 'Number' as integer)` and came
  back in ascending order. Computed keys over the document now translate with the computed `Select()` translation
  (negation, arithmetic including `%`, widening casts, string concatenation, `ToUpper()`/`ToLower()`, `Length`,
  `ToString()` of an integer or a `Guid`). A key with no exact SQL translation, such as decimal division, floating
  point `%`, a narrowing cast (`(byte)x.Number`), a conditional, a `Math` call or an enum stored as a string cast
  to an integer, now throws `BadLinqExpressionException`. Order by a member, or sort in memory after the query.
  A plain member key sends exactly the SQL it did before.
- The same applies, as of [#5968](https://github.com/JasperFx/marten/issues/5968), to a key over the child
  documents a `SelectMany()` flattens, `SelectMany(x => x.Children).OrderBy(c => -c.Index)`, and to a key over
  either side of a `Join()` or `GroupJoin()`, `Join(...).OrderBy(r => -r.Amount)`. Both used to order by the member
  under the negation, and both refused arithmetic such as `% 3`.

### LINQ `Select()` after `SelectMany()` projects the flattened rows <Badge type="warning" text="behavior change" />

- A correctness fix ([#5933](https://github.com/JasperFx/marten/issues/5933)), so these queries return different
  rows or throw where they used to return wrong data. `SelectMany(x => x.Values).Select(v => v % 3)` returned the raw
  values: the projection was dropped whenever it was arithmetic or a method call over the flattened element. A
  `Where()`, `OrderBy()` or `Take()` applied after a projection of child documents was dropped as well.
- The projection is now computed in SQL over the flattened rows, and every operator after it (`Where()`,
  `OrderBy()`, `Distinct()`, `Skip()`/`Take()`, `Count()`, `Any()`, `First()`, `Sum()` and the rest) applies to
  the projected value. Member access, arithmetic, string concatenation, `ToUpper()`, `ToLower()`, `Length`,
  `ToString()` of an integer or a `Guid`, widening casts, enum to integer, and anonymous types or DTOs built from
  those translate.
- A projection with no exact SQL translation, such as a `Math` call, a conditional, decimal division or a narrowing
  cast, now throws `BadLinqExpressionException` instead of returning the raw rows. So does a `Where()`, `OrderBy()`
  or `Distinct()` applied after an object projection of a flattened row. Apply those before the `Select()`, or
  project in memory after `ToListAsync()`. A `SelectMany()` without a `Select()` after it sends exactly the SQL it
  did before.

### LINQ `SelectMany()` over a duplicated array field works

- A bug fix ([#5934](https://github.com/JasperFx/marten/issues/5934)). `SelectMany()` over an array field duplicated
  into its own column (`Duplicate(x => x.Numbers)` or `[DuplicateField]`) failed: fetching failed with `42P01`
  (a CTE that was never declared) and counting with `42883` (`jsonb_array_length()` with no argument). The
  duplicated column is now flattened with `unnest()` and counted with `array_length()`, and `Where()`, `OrderBy()`,
  paging, `First()`/`Single()` and `Select()` after the `SelectMany()` apply as they do to a collection that is not
  duplicated. A `SelectMany()` over a collection that is not duplicated sends exactly the SQL it did before.

### LINQ `SelectMany()` over an enum collection works

- A bug fix ([#5940](https://github.com/JasperFx/marten/issues/5940)). `SelectMany(x => x.Colours)` over an array or
  list of enums threw `InvalidCastException`, because each flattened element was read as the enum type from the
  integer or text column it is stored in. The element is now read as the stored value and converted, under both
  `EnumStorage.AsInteger` and `EnumStorage.AsString`, and `Where()`, `Distinct()`, `First()` and the rest work
  after it. `OrderBy()` sorts by the stored value, so by name under `EnumStorage.AsString`, as an `OrderBy()` on
  an enum member of a document already does. The SQL is unchanged.

### LINQ string `Length` and string methods after `SelectMany()` translate

- A bug fix ([#5941](https://github.com/JasperFx/marten/issues/5941)). After `SelectMany()` over a string collection,
  `Where(n => n.Length == 4)` failed with `42883`, because `Length` was located as a JSON field inside the string.
  `OrderBy(n => n.Length)` and `OrderBy(n => n.ToUpper())` ordered by the string itself. `Length` now translates
  to PostgreSQL's `length()`, on a flattened string and on a string member of a document alike, and `ToUpper()`,
  `ToLower()` and `Trim()` of a flattened string translate in `Where()` and `OrderBy()` as they do for a document's
  member. PostgreSQL's `length()` counts characters where .NET counts UTF-16 code units, so the two differ for a
  string holding an emoji or another character outside the Basic Multilingual Plane.

### LINQ computed top-level `Select()` projections translate

- A bug fix ([#5942](https://github.com/JasperFx/marten/issues/5942)). `Select(x => x.Number % 3)` failed with
  `ArgumentNullException`, `Select(x => x.Id.ToString())` with `ArgumentException`, and a cast such as
  `Select(x => (long)x.Number)` with `NotSupportedException`. Arithmetic, negation, widening casts, enum to
  integer, string concatenation, `ToUpper()`, `ToLower()`, `Length`, and `ToString()` of an integer or a `Guid` are
  now computed in SQL, and `Where()`, `OrderBy()`, `Distinct()`, paging, `Count()`, `First()` and the aggregates
  after the `Select()` apply to the computed value. Anything else, such as a `Math` call, a conditional, decimal
  division or a narrowing cast, throws `BadLinqExpressionException` naming the projection. Member, object and
  `ExplicitSql()` projections send exactly the SQL they did before.

### LINQ `OrderBy()` after a projection orders the projected values <Badge type="warning" text="behavior change" />

- A correctness fix ([#5955](https://github.com/JasperFx/marten/issues/5955)), so these queries return rows in a
  different order, or throw where they used to return them in the wrong order. Over a flattened value collection,
  `SelectMany(x => x.Values).OrderBy(v => -v)` ordered by `v`: any key was taken as the element itself. The key is
  now computed in SQL. After a top-level `Select()`, `Select(x => x.Number).OrderBy(n => n)` dropped the
  `OrderBy()`. The projected values are now ordered, and `Skip()`/`Take()` after the ordering page them.
- An `OrderBy()` after an anonymous type, a DTO or a projection to another document now throws
  `BadLinqExpressionException`. It used to be dropped, or for a projection to the document's own type, such as
  `Select(x => x.Inner)`, it was applied to the documents. Order before the `Select()` instead. A key that has no
  exact SQL translation, such as a `Math` call, is refused the same way.

### `UseStickyConnectionLifetimes` is gone <Badge type="warning" text="breaking" />

`StoreOptions.UseStickyConnectionLifetimes`, which brought back the Marten 6 behavior of holding one connection open for
the whole life of every session, has been removed. Sessions use and release connections as they go.

When one session needs to keep one connection, ask for it on that session: `await session.BeginTransactionAsync()` or
`await session.GetConnectionAsync()` pins the session to a connection until it is disposed, and the serializable
sessions (`OpenSerializableSessionAsync()`, `QuerySerializableSessionAsync()`) and a caller-supplied connection or
transaction (`SessionOptions.ForConnection()`, `SessionOptions.ForTransaction()`) do the same.

### `AllDocumentsAreMultiTenantedWithPartitioning(configure)` uses `(tenant_id, id)` primary keys <Badge type="warning" text="schema change" />

The overload of `Policies.AllDocumentsAreMultiTenantedWithPartitioning()` that takes only the partitioning lambda now
uses `PrimaryKeyTenancyOrdering.TenantId_Then_Id`, the same primary key as the per-document
`MultiTenantedWithPartitioning()`. In 9.x it used `Id_Then_TenantId`, so the next migration rewrites the primary key of
every table this policy partitions. To keep the old primary key, pass it:

```cs
opts.Policies.AllDocumentsAreMultiTenantedWithPartitioning(
    x => x.ByExternallyManagedListPartitions(),
    PrimaryKeyTenancyOrdering.Id_Then_TenantId);
### Declaring the same computed index twice throws <Badge type="warning" text="behavior change" />

Two `Index()` calls on one document type that resolve to the same index name, for example the same member twice,
or the same member again with `Casing = Lower`, now throw an `InvalidOperationException` while the document is
configured. The message names the document type, the member and the index. In 9.x both were accepted and the
failure came much later, when a migration was computed against an existing table, as an
`ArgumentException: An item with the same key has already been added` that named only the index
([#6019](https://github.com/JasperFx/marten/issues/6019)).

Declare the index once. If you really want a second index over the same member, give it its own name:

```cs
opts.Schema.For<Order>().Index(x => x.CustomerEmail);
opts.Schema.For<Order>().Index(x => x.CustomerEmail, x =>
{
    x.Casing = ComputedIndex.Casings.Lower;
    x.Name = "idx_order_customer_email_lower";
});
```

### Stream compaction waits for async projections and subscriptions <Badge type="warning" text="behavior change" />

`CompactStreamAsync()` now throws an `InvalidOperationException` while an asynchronous projection or subscription has
not processed the events it would delete, including one that has never run. Nothing is compacted; compact again once
they caught up. A single stream projection of the compacted type is not checked, since it reads the `Compacted<T>`
snapshot. In 9.x those events were deleted anyway, and a multi-stream projection or a subscription that was behind
never saw them ([#6050](https://github.com/JasperFx/marten/issues/6050), [compacting](/events/compacting)).

### Quick appends lock the stream row before writing events <Badge type="warning" text="schema change" />

The `mt_quick_append_events` function now locks the stream's `mt_streams` row (`for no key update`) when it reads the
stream version, before it inserts any events. Before, it took that lock last, and concurrent appends to one stream
could deadlock each other, or a `FetchForExclusiveWriting()` of that stream, with PostgreSQL error `40P01`
([#6066](https://github.com/JasperFx/marten/issues/6066)). Now appends to one stream wait their turn. An append with an
expected version that waited throws the usual concurrency exception without drawing a sequence number, and a plain
`Append()` goes in after the append it waited for instead of failing.

The function changes, so schema migration replaces it: `AutoCreate.CreateOrUpdate` and `All` do that on their own, and
a `db-patch` contains one `CREATE OR REPLACE FUNCTION` for it. `UseExclusiveLockOnConcurrentAppends` still works and
takes `for update` instead, but there is little reason left to turn it on ([appending](/events/appending)).

### A snapshot without its stream throws `SnapshotWithoutStreamException` <Badge type="warning" text="behavior change" />

For an aggregate projected with `ProjectionLifecycle.Async`, `FetchForWriting()`, `FetchForExclusiveWriting()`,
`FetchLatest()` and `StreamLatestJson()` now throw `SnapshotWithoutStreamException` when the aggregate's snapshot
document exists but its stream does not. That happens after `DeleteAllEventDataAsync()`, which leaves documents in
place. In 9.x `FetchForWriting()` returned the old snapshot at version 0, as if the stream were new. Delete the
snapshot documents along with the events, for example with `DeleteDocumentsByTypeAsync()`
([cleaning](/schema/cleaning), [#5799](https://github.com/JasperFx/marten/pull/5799)).

### Batched fetches of `Async` aggregates have no ordering rules <Badge type="tip" text="new" />

An `Async` aggregate fetch now reads the events after the snapshot before it reads the snapshot itself, so it no
longer wraps its reads in `begin transaction isolation level repeatable read read only` … `end`. That lifts the rules
the bracket forced on batches in 9.x:

- a batched `FetchForWriting()` or `FetchLatest()` no longer has to be the first item in its batch,
- it can share a batch with `FetchForExclusiveWriting()`, and with other `Async` fetches,
- `FetchManyForWriting()` reads every stream in one round trip instead of one fetch per stream.

The `InvalidOperationException`s that refused those batches are gone. A batched fetch whose snapshot went backwards
between its two reads, as it does while a projection is rebuilt, still throws `SnapshotRaceException`; retry the
batch. Outside a batch Marten redoes the fetch itself, and `FetchManyForWriting()` falls back to one fetch per stream
([#5799](https://github.com/JasperFx/marten/pull/5799)).

### A `Live` aggregate's `FetchForWriting()` reports the newest version it read <Badge type="warning" text="behavior change" />

`FetchForWriting()` on a `Live` aggregate now reports the newest stream version its two reads saw, the rule `Async`
has followed since #5799. Before, an append that committed between the version read and the events read was folded
into the aggregate while the stream reported the older version, so the save failed against an append the aggregate
already contained ([#6079](https://github.com/JasperFx/marten/pull/6079), [#5225](https://github.com/JasperFx/marten/issues/5225)).

### `RebuildSingleStreamAsync()` goes through the daemon, and refuses when another node runs the projection <Badge type="warning" text="behavior change" />

For a document written by an asynchronous single-stream projection, every `Advanced.RebuildSingleStreamAsync()`
overload now picks one of three paths ([#6071](https://github.com/JasperFx/marten/issues/6071)):

1. A daemon of this store in this process runs the projection's shard: the rebuild goes through it and is committed
   with the shard's next batch, so it cannot race the shard. The call returns when that batch commits.
2. Otherwise, if the shard's progression row was written in the last 30 seconds by a running agent, the call throws
   `IdentityRebuildRefusedException` with `Reason == IdentityRebuildRefusal.ShardLiveOnAnotherNode`. In 9.x it wrote
   the document anyway and raced the other node's daemon. Rebuild on the node that runs the projection, or with
   `IProjectionDaemon.RebuildIdentityAsync()` there.
3. Otherwise the stream is aggregated and stored here, as in 9.x. That is only safe when no daemon runs the
   projection anywhere.

Inline and live projections always take the third path. A stream that folds to nothing now deletes the document; in
9.x that threw `ArgumentNullException` from `Store(null)`. The call also commits on its own; the old doc comment
saying you had to call `SaveChangesAsync()` was wrong. See [rebuilding a single stream](/events/projections/rebuilding#rebuilding-a-single-stream).

### New Guid ids sort after existing ones, and do not carry a readable timestamp <Badge type="warning" text="behavior change" />

Marten now makes new Guid ids with JasperFx's `SequentialGuid.NextPostgresOrder()`: document ids for
`SequentialGuidIdGeneration` (the default for `Guid` ids), event ids, stream ids Marten makes for natural keys, and the
`mt_version` Guids. The ids are strictly ordered within a process and sort the way PostgreSQL compares a `uuid`, so
inserts go to the end of the index. Ids made by Marten 10 sort after ids made by Marten 9 (COMB) and after
`Guid.CreateVersion7()` ids, so existing data keeps its order.

They are not version 7 Guids and not COMB Guids. Don't read a timestamp out of an id
(`CombGuidIdGeneration.GetTimestamp()` does not read the new ones). Use the document's `mt_last_modified` or the
event's `Timestamp` instead.

### Read-your-writes: `LastAppendedPosition`

The session's position after `SaveChangesAsync()` is `session.LastAppendedPosition` (JasperFx 3.6.0).
`LastAppendedSequence`, its name in JasperFx 3.5.0, is an obsolete alias that returns the same value. See
[reading your own writes](/events/projections/async-daemon#reading-your-own-writes).

### Database provisioning has its own command timeout <Badge type="tip" text="new" />

`CREATE DATABASE` and `DROP DATABASE` now run under a provisioning command timeout, two minutes by default, instead of
the store's command timeout, which is usually tuned for queries
([#6060](https://github.com/JasperFx/marten/issues/6060)). It only ever lengthens the timeout. Set it on the
single-server tenancy (`ProvisioningCommandTimeout`), on `CreateDatabasesForTenants()`
(`ProvisioningCommandTimeout(TimeSpan)`), and for extension installs on `opts.Advanced.Migrator`. See
[provisioning timeouts](/configuration/multitenancy#provisioning-command-timeout).

### Default changes in 10.0 and how to restore Marten 9 behavior

| Change | Restore Marten 9 behavior |
| --- | --- |
| [`EnableExtendedProgressionTracking`](#enableextendedprogressiontracking-is-on-by-default) is on | `opts.RestoreV9Defaults()`, or `opts.Events.EnableExtendedProgressionTracking = false` |
| [DCB conflicts scoped by event type](#dcb-conflicts-are-scoped-by-event-type-by-default-) (`DcbConflictScope.TagValueAndEventType`) | `opts.RestoreV9Defaults()`, or `opts.Events.DcbConflictScope = DcbConflictScope.TagValue` |
| [Side-effect gate](/events/projections/rebuilding#suppressing-side-effects-during-the-blue-green-warm-up) (`GateSideEffectsBehindPriorVersion`, jasperfx#1075) is on | `opts.RestoreV9Defaults()`, or `GateSideEffectsBehindPriorVersion = false` on the projection |
| [**Side effects run on inline projections**](#side-effects-run-on-inline-projections) (`EnableSideEffectsOnInlineProjections`) | `opts.RestoreV9Defaults()`, or `opts.Events.EnableSideEffectsOnInlineProjections = false` |
| [Archived-stream partitioning and strict stream identity](#archived-stream-partitioning-is-on-by-default) are on (`UseArchivedStreamPartitioning`, `EnableStrictStreamIdentityEnforcement`); schema change | `opts.RestoreV9Defaults()`, or set both to `false` on `opts.Events` |
| [Document metadata columns](#document-metadata-is-on-by-default) `correlation_id`, `causation_id`, `headers`, `last_modified_by` are on; schema change | `opts.RestoreV9Defaults()`, or `opts.Policies.ForAllDocuments(x => x.Metadata.CausationId.Enabled = false)` and the same for the other three |
| [Computed indexes on conjoined documents start with `tenant_id`](#conjoined-indexes-start-with-tenant-id) (`StartIndexesByTenantId`); schema change | `opts.RestoreV9Defaults()`, or `opts.Policies.ForAllDocuments(x => x.StartIndexesByTenantId = false)` |
| [All event metadata](#all-event-metadata-is-on-by-default) (causation id, correlation id, headers, user name) is on | `opts.RestoreV9Defaults()`, or set the `opts.Events.MetadataConfig.*Enabled` flags to `false` |
| [Skipped events are filtered](#event-skipping-is-on-by-default) (`EnableEventSkippingInProjectionsOrSubscriptions`) | `opts.RestoreV9Defaults()`, or `opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = false` |
| [Event counters and `FetchForWriting` metrics](#metrics-are-on-by-default) are exported | `opts.RestoreV9Defaults()`, or `opts.OpenTelemetry.EnableEventCounters = false` and `opts.OpenTelemetry.EnableFetchForWritingMetrics = false` |
| [System.Text.Json `AllowOutOfOrderMetadataProperties`](#allow-out-of-order-metadata-properties) is on | `opts.RestoreV9Defaults()`, or `o.AllowOutOfOrderMetadataProperties = false` in the `configure` callback of `UseSystemTextJsonForSerialization()` |
| [Async projection errors park the failing identity](#async-projections-park-a-failing-identity), and pause where they can't park (`Projections.Errors.ApplyErrors` and `SerializationErrors` are `Park`; [JasperFx docs](https://shared-libs.jasperfx.net/events/parked-identities)); new `mt_parked_identities` table | `opts.RestoreV9Defaults()`, or set both to `ProjectionErrorAction.Skip` |
| [The async daemon builds a batch again after a transient failure](/configuration/retries#the-async-daemon-builds-a-failed-batch-again) (a timeout, a dropped connection, an exhausted pool), up to 3 times under every error policy, then pauses the shard with `IsTransient = true` ([JasperFx 3.11.0](https://shared-libs.jasperfx.net/migration-guide#key-changes-in-3-11-0)) | none |
| [Cheap event-store objects always created](#event-store-objects-that-used-to-depend-on-configuration-are-always-created-) | none; schema change, see below |
| [New `mt_event_range_marks` table](#new-mt-event-range-marks-table) for stored-event rewrites | none; schema change, see below |

`opts.RestoreV9Defaults()` is the single opt-out for every `StoreOptions` default flipped in 10.0
([details](#restoring-v9-defaults)).

Defaults that already turned on during 9.x minors, for readers coming from an older 9.x: the daemon's connection
governors (`MaxConcurrentEventLoadsPerDatabase` and `MaxConcurrentBatchWritesPerDatabase`, both 4, since 9.13;
[details](/events/projections/async-daemon#daemon-connection-governors)) and `StreamOne<T>` emitting an `ETag`
(opt out with `EmitETag = false`; [details](/documents/aspnetcore)).

### Event-store objects that used to depend on configuration are always created <Badge type="warning" text="schema change" />

Two stores, or two applications, sharing one schema with different settings used to drop each other's
event-store objects when either applied its schema (#5309). The cheap ones are now part of the schema
whatever the flags say; the flags keep governing *behavior* only:

| Object | Used to exist when | Now |
| --- | --- | --- |
| `mt_events.is_skipped` column | `Events.EnableEventSkippingInProjectionsOrSubscriptions` | always |
| `mt_events` `correlation_id`, `causation_id`, `headers`, `user_name` columns | the matching `Events.MetadataConfig.*Enabled` flag | always (nullable) |
| `mt_high_water_skips` table and `mt_mark_progression_with_skip` function | `Events.EnableAdvancedAsyncTracking` | always |

The first schema apply after upgrading adds whichever of these were missing. Adding a nullable column to
`mt_events` is a metadata-only change in PostgreSQL. Reads and writes are unchanged: events are only skipped
when skipping is enabled, and metadata is only written and selected when its flag is on.

Deliberately still conditional, because they are expensive or change the table's shape: tenant partitioning of
`mt_events`, table fillfactor, and the DCB `hstore` tags column and its GIN index.

### New `mt_event_range_marks` table {#new-mt-event-range-marks-table} <Badge type="warning" text="schema change" />

The event store schema has a new table, `mt_event_range_marks`. It holds where a stored-event rewrite stops and how
far it got, so a rewrite run as a Wolverine job can resume after a crash without rewriting an event twice. It's always
created, whatever the configuration, and `RestoreV9Defaults()` doesn't remove it. Rebuilds and the `Clean` methods
leave its rows alone; removing a tenant deletes that tenant's rows. Under `AutoCreate.None`, apply the migration before
deploying.

### DCB tag versions come from a sequence, and a tombstone erases tag values <Badge type="warning" text="schema change" />

If you register a DCB tag type, the schema gains a sequence, `mt_dcb_tag_version_sequence`, and every new
`mt_dcb_tag_version` version comes from it ([#6035](https://github.com/JasperFx/marten/issues/6035)). It's additive, so
any `AutoCreate` setting but `None` adds it on first use, and existing rows keep working. Under `AutoCreate.None`, apply
the migration before deploying, because tagged appends fail without the sequence.

- `TombstoneStream` deletes a tag's `mt_dcb_tag_version` row once no other stream carries it, and never creates one. See
  [what a tombstone erases](/events/dcb#what-a-tombstone-erases).
- `mt_dcb_tag_version.version` is no longer a count of commits.
- Two boundaries in one session over the same tag now conflict when they read different versions.
- Archiving, un-archiving and `AssignTagWhere` move the versions of the tags they touch, and create a row for a tag
  that has none. `DeleteSingleEventStreamAsync` moves and deletes rows the way `TombstoneStream` does
  ([#6078](https://github.com/JasperFx/marten/issues/6078)).
- Finish a rolling upgrade before relying on this: a node still on Marten 9 counts up from the current value and doesn't
  delete rows.

### `mt_high_water_skips` has a tenant column, and late events are recovered {#late-events-are-recovered} <Badge type="warning" text="schema change" />

`mt_high_water_skips` gets new columns: `tenant_id`, which is now part of its primary key, `reason`, `gaps` and
`closed_at`. There is a new table, `mt_high_water_late_events`. The `mt_mark_progression_with_skip` function changes
to match the new key. The first schema apply after upgrading makes these changes and keeps the existing rows.

The daemon now checks skipped sequence ranges for events that commit late, records them, and rebuilds their streams
in async single-stream projections ([details](/events/projections/async-daemon#events-that-commit-late)).
`opts.Events.EnableAdvancedAsyncTracking = false` turns that off. The schema change stays either way.

### `MetadataConfig.EnableAll()` also enables the user name {#metadataconfig-enableall-also-enables-the-user-name} <Badge type="warning" text="behavior change" />

`opts.Events.MetadataConfig.EnableAll()` now turns on `UserNameEnabled` along with the causation id, correlation id
and headers ([#6012](https://github.com/JasperFx/marten/pull/6012)). In 9.x it left the user name off, despite
documenting itself as enabling all optional metadata. A store that calls `EnableAll()` now writes the session's
`LastModifiedBy` to `mt_events.user_name` on every appended event, and reads it back as `IEvent.UserName`.

There's no schema change: the `user_name` column is always created in 10.0 (see above). Events are never
rewritten, so if your `LastModifiedBy` holds a person's name or login and you have data-retention rules, decide
before upgrading whether you want it stored. To keep the 9.x behavior, turn it off after `EnableAll()`:

```cs
opts.Events.MetadataConfig.EnableAll();
opts.Events.MetadataConfig.UserNameEnabled = false;
```

### `StreamLockedException` now lives in `JasperFx.Events` and derives from `ConcurrencyException`

`Marten.Exceptions.StreamLockedException` is gone; Marten now throws `JasperFx.Events.StreamLockedException`, the
type shared with Polecat so that Wolverine.Http can map both to one `409 Conflict`. The message is unchanged, and the
stream id is available as `StreamId` (and `ConcurrencyException.Id`).

- It derives from `JasperFx.ConcurrencyException`, so `catch (ConcurrencyException)` now also catches a lock timeout.
- It no longer derives from `MartenException`, so `catch (MartenException)` no longer sees it.
- The type moved namespaces, so assemblies compiled against Marten 9 must be recompiled, and source that names `Marten.Exceptions.StreamLockedException` needs `using JasperFx.Events;` instead.

### Marten throws the shared `JasperFx.Events` exception types

Marten's own copies of the exception types that Marten, Polecat and Fisher share are gone (#5847). Marten now throws the
`JasperFx.Events` (or `JasperFx`) type directly, with no Marten subclass, so a store-agnostic `catch` works the same
on all three stores:

| Marten 9 | Marten 10 |
| --- | --- |
| `Marten.Exceptions.ExistingStreamIdCollisionException` | `JasperFx.Events.ExistingStreamIdCollisionException` |
| `Marten.Exceptions.NonExistentStreamException` | `JasperFx.Events.NonExistentStreamException` |
| `Marten.Exceptions.UnknownEventTypeException` | `JasperFx.Events.UnknownEventTypeException` |
| `Marten.Exceptions.EventDeserializationFailureException` | `JasperFx.Events.EventDeserializationFailureException` |
| `Marten.Exceptions.BadLinqExpressionException` | `JasperFx.BadLinqExpressionException` |
| `Marten.Exceptions.InvalidStreamOperationException`, when appending to an archived stream | `JasperFx.Events.ArchivedStreamException` |
| `Marten.Exceptions.ApplyEventException` (`[Obsolete]`, never thrown since 9.x) | removed; catch `JasperFx.Events.Daemon.ApplyEventException` |

- None of these derive from `MartenException` any longer, so `catch (MartenException)` no longer sees them.
  `MartenException` stays the root of Marten-only failures, and `MartenCommandException` is unchanged.
- The members are the same (`Id`, `AggregateType`, `EventTypeName`, `Sequence`, `Category`), and
  `ArchivedStreamException.Id` carries the stream's id or key.
- Some message text changed to the shared wording. In particular the unknown-event-type message no longer suggests
  `StoreOptions.Projections.Errors.SkipUnknownEvents`, and the nonexistent-stream message no longer mentions
  `UseMandatoryStreamTypeDeclaration`. Match on the type and its members, not on the message.
- Source that names the old types needs `using JasperFx.Events;` (or `using JasperFx;` for `BadLinqExpressionException`),
  and assemblies compiled against Marten 9 must be recompiled.

### A failed save reports the failing operation's exception <Badge type="warning" text="behavior change" />

When one statement of a `SaveChangesAsync()` batch fails, the exception is now translated by the operation that
queued that statement ([#5914](https://github.com/JasperFx/marten/issues/5914)). Before, a neighboring operation
could claim it. Most visibly, under the default `QuickWithServerTimestamps` append mode, a `FetchForWriting()`
append that lost its version check in the same save as a `StartStream()` threw `ExistingStreamIdCollisionException`
for the new stream. It now throws `EventStreamUnexpectedMaxEventIdException` (a `ConcurrencyException`) for the
fetched stream, so retry policies on `ConcurrencyException` see it. A genuine duplicate `StartStream()` is still an
`ExistingStreamIdCollisionException`, now naming the duplicate stream rather than another stream started in the
same save. Under the `Quick` append modes, the version conflict's message now reports the stream's actual version
instead of `-1`.

Sessions with a sticky connection, an external transaction or an ambient `TransactionScope` now also offer a
failure to the failing operation before the transforms in `AdvancedOptions.ExceptionTransforms`, which is the order
the other sessions already used.

### `EnableExtendedProgressionTracking` is on by default

`opts.Events.EnableExtendedProgressionTracking` now defaults to `true`: the async daemon writes
`heartbeat`, `agent_status`, `pause_reason`, `running_on_node` and the `failure_*` columns of
`mt_event_progression`. `IEventStoreInstrumentation.ExtendedProgressionEnabled` is the same switch
under its storage-agnostic name. To keep the 9.x behavior:

```cs
opts.Events.EnableExtendedProgressionTracking = false;
```

No DDL is needed on a database migrated by 9.31 or later (the columns have been unconditional since
9.31). The unused `mt_mark_event_progression_extended` function is no longer part of the schema; a
copy already installed is left in place and can be dropped by hand. The `warning_behind_threshold` and
`critical_behind_threshold` columns are never written; drop them only as a deliberate migration step
(`alter table ... drop column` takes an ACCESS EXCLUSIVE lock on a hot table).

### DCB conflicts are scoped by event type by default <Badge type="warning" text="rolling upgrade" />

`opts.Events.DcbConflictScope` now defaults to `DcbConflictScope.TagValueAndEventType`
([#5887](https://github.com/JasperFx/marten/issues/5887)). A `FetchForWritingByTags` query condition that names
event types with `AndEventsOfType` now only conflicts with later events of those types, as the
[DCB specification](https://dcb.events/specification/) does; Marten 9 conflicted on any later event carrying the
tag value. Expect fewer `DcbConcurrencyException`s. No schema change is involved.

::: danger Rolling upgrades from Marten 9 can miss conflicts
Marten 10 checks a version per (tag value, event type) that Marten 9 nodes do not maintain by default. While any
node is still running Marten 9 with its default, its appends are invisible to a Marten 10 node's boundary check,
and two decisions that should have conflicted can both commit. Before a rolling upgrade, do **one** of:

- deploy Marten 9.60 or later with `opts.Events.DcbConflictScope = DcbConflictScope.TagValueRecordingEventTypes`
  to every node first, then roll out Marten 10; or
- roll out Marten 10 with `opts.Events.DcbConflictScope = DcbConflictScope.TagValueRecordingEventTypes` (or
  `opts.RestoreV9Defaults()`, which sets `TagValue`), and remove that line in a later deployment once no Marten 9
  node is left.

A deployment that stops every node before starting Marten 10 needs neither step. A store that never calls
`FetchForWritingByTags` is unaffected.
:::

To keep the Marten 9 behavior:

```cs
opts.Events.DcbConflictScope = DcbConflictScope.TagValue;
```

### Side effects run on inline projections {#side-effects-run-on-inline-projections} <Badge type="danger" text="behavior change" />

::: danger Inline projections now run their side effects
**`opts.Events.EnableSideEffectsOnInlineProjections` now defaults to `true`. If any of your aggregation projections
override `RaiseSideEffects()` and run `Inline`, that code now runs on every `SaveChangesAsync()` that touches the
projection, inside the same unit of work. Messages it publishes and documents or events it stores go out where they
did not before.** In Marten 9 those side effects only ran in the async daemon.
:::

Check every `SingleStreamProjection` and `MultiStreamProjection` with a `RaiseSideEffects()` override that is
registered with `ProjectionLifecycle.Inline`. Projections running `Async` are unchanged. See
[Side Effects in Inline Projections](/events/projections/side-effects#side-effects-in-inline-projections). To keep the
9.x behavior:

```cs
opts.Events.EnableSideEffectsOnInlineProjections = false;
```

### Archived-stream partitioning is on by default {#archived-stream-partitioning-is-on-by-default} <Badge type="warning" text="schema change" />

`opts.Events.UseArchivedStreamPartitioning` and `opts.Events.EnableStrictStreamIdentityEnforcement` now default to
`true` ([Hot/Cold Storage Partitioning](/events/archiving#hot-cold-storage-partitioning)). `mt_events`, `mt_streams` and
the other event tables are list-partitioned by `is_archived`, so archiving a stream moves its rows to the archived
partitions, and a new `mt_streams_identity` table keeps a stream id from being reused after its stream is archived.

::: warning The first schema migration rebuilds mt_events and mt_streams
On an existing event store, the first schema apply after upgrading turns `mt_events` and `mt_streams` into partitioned
tables. PostgreSQL cannot partition a table in place, so the migration copies each table to a temporary one, drops it,
creates the partitioned table and copies the rows back. That takes time and locks proportional to the size of your
event store. Generate the migration script and run it in a maintenance window, or keep the 9.x shape by turning the
setting off (below) and decide later.
:::

The same migration also releases `mt_events_sequence` from its `OWNED BY mt_events.seq_id` link
(`ALTER SEQUENCE ... OWNED BY NONE`), so rebuilding `mt_events` can no longer drop the sequence along with it. Marten
10 creates the sequence without an owner.

Both settings are off under [per-tenant event partitioning](/events/multitenancy#per-tenant-event-partitioning)
(`UseTenantPartitionedEvents`), which partitions the same tables by tenant. Marten 9 refused that combination at
startup; Marten 10 only refuses it when you set `UseArchivedStreamPartitioning = true` yourself.

Strict stream identity adds one small insert per new stream. The migration that creates `mt_streams_identity` fills it
from `mt_streams`, archived streams included, so streams that existed before the upgrade are covered too
([#6051](https://github.com/JasperFx/marten/issues/6051)). To keep the 9.x behavior:

```cs
opts.Events.UseArchivedStreamPartitioning = false;
opts.Events.EnableStrictStreamIdentityEnforcement = false;
```

Decide this before the first schema migration under Marten 10 if you can. Taking an event store that has already been
partitioned back to unpartitioned works, but Weasel has to rebuild `mt_events` and `mt_streams` to do it, copying
every row ([weasel#871](https://github.com/JasperFx/weasel/issues/871)).

### Document metadata is on by default {#document-metadata-is-on-by-default} <Badge type="warning" text="schema change" />

Every document type now has the `correlation_id`, `causation_id`, `headers` and `last_modified_by` metadata columns, and
every write stores the session's `CorrelationId`, `CausationId`, headers and `LastModifiedBy` in them
([Marten Metadata](/documents/metadata)). In Marten 9 each column was opt in per document type.

The first schema apply after upgrading adds the four nullable columns to every document table and replaces the upsert
functions. Adding a nullable column is a metadata-only change in PostgreSQL. A document type that maps these columns to
its own members, through `ITracked` or `Metadata(...)`, is unaffected.

`last_modified_by` holds whatever `IDocumentSession.LastModifiedBy` is set to. If that is a person's name or login and
you have data-retention rules, decide before upgrading whether you want it stored. To keep the 9.x behavior for all
documents, call `opts.RestoreV9Defaults()`, or turn the columns off with a policy:

```cs
opts.Policies.ForAllDocuments(x =>
{
    x.Metadata.CausationId.Enabled = false;
    x.Metadata.CorrelationId.Enabled = false;
    x.Metadata.Headers.Enabled = false;
    x.Metadata.LastModifiedBy.Enabled = false;
});
```

`RestoreV9Defaults()` decides this when each document mapping is built, so a policy, attribute or
`Schema.For<T>().Metadata(...)` that turns one of the columns back on still does, wherever it is in your configuration.

### Computed indexes on conjoined documents start with `tenant_id` {#conjoined-indexes-start-with-tenant-id} <Badge type="warning" text="schema change" />

`StartIndexesByTenantId` now defaults to `true` for conjoined (multi-tenanted) documents
([details](/documents/multi-tenancy#make-all-documents-start-their-index-by-tenant-id)). Every computed index on such a
document leads with `tenant_id`, so a query in one tenant's session reads only that tenant's index entries. Single
tenanted documents are unchanged.

- A unique index scoped `TenancyScope.PerTenant` now leads with `tenant_id` instead of ending with it.
- A unique index scoped `TenancyScope.Global` (the default for `UniqueIndex()` and `[UniqueIndex]`) does **not** get
  `tenant_id`, so it stays unique across tenants. This also applies when you set `StartIndexesByTenantId` yourself;
  in Marten 9 that setting quietly turned a global unique index into a per-tenant one, and the first migration now
  puts it back to global, which fails if two tenants already share a value.

The first schema apply after upgrading drops and recreates every computed index on a conjoined document. Building an
index on a large table takes time and blocks writes to it, so generate the migration script and run it in a maintenance
window if that matters to you. To keep the 9.x indexes:

```cs
opts.Policies.ForAllDocuments(x => x.StartIndexesByTenantId = false);
```

### All event metadata is on by default {#all-event-metadata-is-on-by-default}

`opts.Events.MetadataConfig` now has `CausationIdEnabled`, `CorrelationIdEnabled`, `HeadersEnabled` and
`UserNameEnabled` all `true` ([Event Metadata](/events/metadata)). Every appended event stores the session's
causation id, correlation id, headers and `LastModifiedBy`, and `IEvent` reads them back. There's no schema change: the
columns are always created in 10.0 (see above). As with [`EnableAll()`](#metadataconfig-enableall-also-enables-the-user-name),
events are never rewritten, so decide whether you want a user name in `mt_events` before upgrading. To keep the 9.x
behavior:

```cs
opts.Events.MetadataConfig.CausationIdEnabled = false;
opts.Events.MetadataConfig.CorrelationIdEnabled = false;
opts.Events.MetadataConfig.HeadersEnabled = false;
opts.Events.MetadataConfig.UserNameEnabled = false;
```

### Skipped events are filtered by default {#event-skipping-is-on-by-default}

`opts.Events.EnableEventSkippingInProjectionsOrSubscriptions` now defaults to `true`. Events marked as skipped are left
out of projections, subscriptions, `AggregateStreamAsync()`, `FetchLatest()` and `FetchForWriting()`
([Marking Events as Skipped](/events/skipping)). Nothing is skipped until you mark an event, so the visible change is
the `is_skipped = FALSE` filter on those event reads. No schema change: the column is always created in 10.0. To keep
the 9.x behavior:

```cs
opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = false;
```

### Event counters and `FetchForWriting` metrics are on by default {#metrics-are-on-by-default}

`opts.OpenTelemetry.EnableEventCounters` (the `marten.event.append` counter) and
`opts.OpenTelemetry.EnableFetchForWritingMetrics` (the `marten.fetch_for_writing.events_replayed` histogram) are new
and default to `true` ([Open Telemetry](/otel)). Both are cheap when nothing listens to the "Marten" meter.
`TrackEventCounters()` and `TrackFetchForWritingMetrics()` still compile and now just set these to `true`. To keep the
9.x behavior:

```cs
opts.OpenTelemetry.EnableEventCounters = false;
opts.OpenTelemetry.EnableFetchForWritingMetrics = false;
```

### System.Text.Json allows out-of-order metadata properties {#allow-out-of-order-metadata-properties}

Marten's System.Text.Json serializer now sets `AllowOutOfOrderMetadataProperties = true`. `jsonb` does not keep
property order, so a polymorphic type (`[JsonDerivedType]`) could fail to read when `$type` was not stored first
([details](/configuration/json)). It only affects types that carry `$type`, `$id` or `$ref` metadata. To keep the 9.x
behavior:

```cs
opts.UseSystemTextJsonForSerialization(configure: o => o.AllowOutOfOrderMetadataProperties = false);
```

## Key Changes in 9.61.0

### Sharded tenancy never reassigns a tenant that already has a shard <Badge type="warning" text="behavior change" />

With `MultiTenantedWithShardedDatabases`, a store that had not loaded the database pool (no `AddDatabase()`, no
`BuildDatabases()`) could resolve a tenant that was already assigned to a shard by running the assignment strategy
again and overwriting the assignment. The tenant moved to another shard while its data stayed on the old one, and
nothing reported it ([#5995](https://github.com/JasperFx/marten/issues/5995)). Two processes racing to assign the
same new tenant could end up on different shards the same way.

An existing assignment is now final. When the assigned shard isn't cached, Marten loads that one shard from the pool
table, and your assignment strategy only runs for a tenant with no assignment at all. If the pool no longer contains
the assigned shard, Marten throws the new `Marten.Exceptions.AssignedShardNotFoundException`. It names the tenant and
the shard (`TenantId`, `DatabaseId`), and the tenant is not reassigned. To fix it, register the shard in the pool
again or assign the tenant explicitly. The assignment table's foreign key to the pool normally prevents this, so
you'll only see it if a pool row was removed some other way.

No schema change is involved.

## Key Changes in 9.60.0

### A DCB boundary save no longer conflicts with boundaries it did not change <Badge type="warning" text="behavior change" />

A `FetchForWritingByTags` boundary now fails with `DcbConcurrencyException` only when an event carrying
a tag value it read was appended after the read. Before 9.60 a boundary save also bumped the version of
every tag value it read, so two boundaries reading the same tag value always conflicted, even when
neither appended anything the other reads
([#5887](https://github.com/JasperFx/marten/issues/5887)). Expect fewer `DcbConcurrencyException`s
under contention. Nothing that was a conflict under the DCB append condition stops being one.

Two saves that each read a tag value the other appended to could also deadlock (PostgreSQL error
`40P01`), surfacing as a `MartenCommandException` or as a one-second stall before a write retry. They
now lock their rows in one order, and the loser fails with `DcbConcurrencyException`.

No schema change is involved, and nodes on 9.59 and 9.60 can run side by side: an older node only
reports more conflicts, never fewer.

### Opt-in: scope DCB conflicts by event type <Badge type="tip" text="new" />

`opts.Events.DcbConflictScope = DcbConflictScope.TagValueAndEventType` makes the conflict check honour
the event types a query condition names with `AndEventsOfType`, as the DCB specification does. The
default stays `DcbConflictScope.TagValue`. Turning it on for a running system takes **two
deployments**, through `DcbConflictScope.TagValueRecordingEventTypes` — a rolling deployment straight
to `TagValueAndEventType` can miss conflicts. See
[scoping conflicts by event type](/events/dcb#scoping-conflicts-by-event-type).

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

Between 9.41 and 9.45 the new default had a second consequence, since fixed in 9.46: stopping a
`HotCold` daemon that held many projection locks could intermittently stall for 60 or 120 seconds
while the locks were released, silently
([#5567](https://github.com/JasperFx/marten/issues/5567)). If you are on one of those versions and
cannot upgrade, either `UseAdvisoryLockTransaction = true` or `UseMonitoredAdvisoryLock = false`
avoids it — prefer the first, since the second gives up fail-over detection.

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

- **Default (`AutoCreate.CreateOrUpdate` or `All`)** — the function is refreshed the next time
  Marten ensures event storage exists. Nothing to do; it is a `CREATE OR REPLACE FUNCTION` with no
  lock on your event data.
- **`AutoCreate.None` with `db-patch` / `db-apply` pipelines** — your next patch will contain one
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

- [Upgrading from Marten 8](#upgrading-from-marten-8): the shortest path to running Marten 9 with the
  same behavior you have today.
- [Breaking changes](#breaking-changes-in-9-0-0): everything that can stop your code compiling or change
  how it behaves.
- [New features](#new-features-in-9-0-0): additions you can adopt when you are ready. None of them are
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

- **`mt_version` becomes `bigint`.** Document tables with numeric revisions are altered from `integer`,
  and the `mt_upsert_*`, `mt_update_*` and `mt_overwrite_*` functions are rewritten to match. Existing
  values are kept.
- **Event sequence and version functions become `bigint`** when `EnableBigIntEvents` is on, which is the
  new default. See [Changed defaults](#changed-defaults).
- **`mt_streams` loses its `snapshot` and `snapshot_version` columns** in new databases. Marten never
  used them. Existing databases keep them, because Marten does not drop columns automatically. They are
  harmless, but you can remove them once per event store schema
  ([#4316](https://github.com/JasperFx/marten/issues/4316)):

  ```sql
  ALTER TABLE my_schema.mt_streams DROP COLUMN snapshot;
  ALTER TABLE my_schema.mt_streams DROP COLUMN snapshot_version;
  ```

#### Restoring V9 defaults {#restoring-v9-defaults}

Marten 10 follows the same rule: every changed default can be put back with one call,
`opts.RestoreV9Defaults()`. It does not call `RestoreV8Defaults()`. `RestoreV9Defaults()` puts these
`StoreOptions` settings back to their Marten 9 values:

| Setting | Marten 9 value | Docs |
| --- | --- | --- |
| `Events.EnableExtendedProgressionTracking` | `false` | [Async Projection Daemon](events/projections/async-daemon.md) |
| `Events.DcbConflictScope` | `TagValue` | [Dynamic Consistency Boundaries](events/dcb.md#scoping-conflicts-by-event-type) |
| `GateSideEffectsBehindPriorVersion` on every registered projection | `false` | [Rebuilding](events/projections/rebuilding.md) |
| `Events.EnableSideEffectsOnInlineProjections` | `false` | [Side Effects](events/projections/side-effects.md#side-effects-in-inline-projections) |
| `Events.UseArchivedStreamPartitioning` and `Events.EnableStrictStreamIdentityEnforcement` | `false` | [Archiving](events/archiving.md#hot-cold-storage-partitioning) |
| Document metadata `CausationId`, `CorrelationId`, `Headers` and `LastModifiedBy` | off | [Marten Metadata](documents/metadata.md) |
| `StartIndexesByTenantId` on conjoined documents | `false` | [Multi-Tenancy](documents/multi-tenancy.md#make-all-documents-start-their-index-by-tenant-id) |
| `Events.MetadataConfig` causation id, correlation id, headers and user name | `false` | [Event Metadata](events/metadata.md) |
| `Events.EnableEventSkippingInProjectionsOrSubscriptions` | `false` | [Marking Events as Skipped](events/skipping.md) |
| `OpenTelemetry.EnableEventCounters` and `OpenTelemetry.EnableFetchForWritingMetrics` | `false` | [Open Telemetry](otel.md) |
| System.Text.Json `AllowOutOfOrderMetadataProperties` | `false` | [JSON Serialization](configuration/json.md) |
| `Projections.Errors.ApplyErrors` and `Projections.Errors.SerializationErrors` | `Skip` | [Async Projection Daemon](events/projections/async-daemon.md#parking-a-failing-identity) |

The side-effect gate is a per-projection setting in JasperFx, so `RestoreV9Defaults()` turns it off for every
projection registered when the store is built, overriding a projection's own value. The document metadata and
`StartIndexesByTenantId` defaults are decided when each document mapping is built, so a policy, attribute or
`Schema.For<T>()` setting wins wherever it is. The serializer setting is applied to the serializer Marten builds and to
one you pass in, whichever comes first, and a `configure` callback given to `UseSystemTextJsonForSerialization()` still
wins. Each 10.0 default flip adds its row here and its test to `RestoreV9DefaultsTests`.

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

- **Drop the Marten part of `codegen write`.** Delete any pre-generated `Internal/Generated/` folders and
  their `.gitignore` entries. Wolverine and other JasperFx tools still generate code, so keep the step if
  they need it.
- **Remove the code generation settings.** `StoreOptions.GeneratedCodeMode`,
  `SourceCodeWritingEnabled`, `GeneratedCodeOutputPath` and `AllowRuntimeCodeGeneration` no longer exist.
  That includes `x.Production.GeneratedCodeMode` in `CritterStackDefaults()`; the `ResourceAutoCreate`
  settings there are unchanged. `StoreOptions.ApplicationAssembly` stays, since assembly scanning uses it.
- **Update links to the pre-building page.** `/configuration/prebuilding` no longer exists. For
  ahead-of-time work, use [source-generated compiled queries](#source-gen-compiled-queries).
- **Expect mapping errors on first use.** Document mappings are now built the first time a session uses
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
| `private SomeAggregate()` | Called | Called, with its field initializers, from JasperFx 3.3.0 on (see below) |

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

A non-public parameterless constructor is the exception to the rule above. The generated dispatcher
calls a `private`, `protected` or `internal` parameterless constructor through a generated
`[UnsafeAccessor]`, so the constructor body and every field and property initializer run, as in Marten 8
([jasperfx#1188](https://github.com/JasperFx/jasperfx/issues/1188), JasperFx 3.3.0). Marten 9 and the
10.0 alphas created such an aggregate without running any constructor; see
[Private aggregate constructors run again](#private-aggregate-constructors-run-again) for what that
changes if you are coming from them. Three shapes are still created without running a constructor, so
their initializers do not run and `Apply` must not depend on them:

- a type with **no** parameterless constructor at all, such as a record with a primary constructor;
- a generic aggregate type;
- an aggregate type nested inside a `private` or `protected` type.

This covers `Apply`, `Create` and `ShouldDelete` on self-aggregating types and on
`SingleStreamProjection` and `MultiStreamProjection` subclasses, `Project` and `ProjectAsync` on
`EventProjection` subclasses, and constructors that take an event. Properties can still have private
setters; only the members Marten calls need to be public.

##### How the event parameter is found {#event-parameter-naming}

When converting lambdas to methods, name the event parameter whatever you like. Marten picks it by type:

- A parameter of type `IEvent<T>` is the event, with event type `T`. Use this form when you need
  [event metadata](/events/metadata).
- Otherwise, the event is the one concrete parameter that is not the aggregate, `IEvent`, a
  `CancellationToken`, or a session interface such as `IQuerySession` or `IDocumentOperations`.

The name only matters when a signature has more than one candidate. Then Marten looks for a parameter
called `@event`, `event`, `e` or `ev`. See
[How Marten Identifies the Event Argument](/events/projections/conventions#how-marten-identifies-the-event-argument).

##### Fewer registration-time errors {#aggregation-validation-rules}

Marten 8 rejected some invalid projections when they were registered. Marten 9 does not, so check for
these yourself:

- **Methods with other names** on a projection class are ignored instead of throwing
  `InvalidProjectionException`. Rename them, or mark them `[JasperFxIgnore]`.
- **An `Apply` on a projection class without an aggregate parameter** is called, but cannot change the
  aggregate. Add the aggregate parameter.
- **A `SingleStreamProjection` whose document type is soft-deleted** is no longer rejected by
  `ValidateConfiguration`.

##### Composite projections have a single shard name

A composite projection now reports one shard name, `<projection-name>/all/v<version>` (for example
`trips/all/v2`), instead of one per member projection
([#4440](https://github.com/JasperFx/marten/issues/4440)). Its members run in sequence against shared
state, so the whole composite has to run on one node; separate shard names let its members be
assigned to different nodes.

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

- `Load<T>`, `LoadMany<T>`, `Query<T>(sql, ...)` and the `Json` methods on `IQuerySession` become
  `LoadAsync`, `LoadManyAsync`, `QueryAsync` and the `Json.*Async` methods.
- `PagedList<T>.Create()` and `PagedList<T>.Init()` throw; use `ToPagedListAsync()`.
- `IQueryHandler<T>` no longer has a synchronous `Handle(DbDataReader, IMartenSession)`. Delete your
  override and implement only `HandleAsync`.
- `ExecuteHandler<T>()` throws; use `ExecuteHandlerAsync()`.

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

- a custom `IRevisionedOperation`, whose `Revision` is now `long`;
- a custom `IBulkLoader<T>` for a revisioned document, which must write the expected version as
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

- `Marten.Internal.Operations.OperationRole` is now `Weasel.Core.OperationRole`
  ([#4350](https://github.com/JasperFx/marten/issues/4350)).
- `Marten.BulkInsertMode` is now `Weasel.Core.BulkInsertMode`
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

- `IEventStore.TeardownExistingProjectionProgressAsync()`. Use `TeardownExistingProjectionStateAsync()`,
  which has the same signature. The old method always removed the projected documents too, so the
  behavior is the same.
- `MultiStreamProjection.CustomGrouping(IEventSlicer<...>)`. Pass a grouping lambda or an
  `IAggregateGrouper<TId>` instead.
- Every other member that was `[Obsolete]` in Marten 8. Clear your obsolete warnings on Marten 8 before
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

- queries whose SQL needs an `ICompiledQueryAwareFilter`: string `Contains`, `StartsWith` and `EndsWith`,
  `HashSet<T>.Contains`, `Dictionary<,>.ContainsKey`, and counts over child collections;
- generic or nested compiled query types;
- compiled queries in an assembly without `[JasperFxAssembly]`.

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

- 8.0 depends on Npgsql 9 and requires Postgres 13+. Postgres 12 is no longer supported.

- Marten 8 drops support for .NET 6 and .NET 7. Only .NET 8 and 9 are supported at the moment (.NET 10 is untested).

- Marten 8 **eliminated almost all synchronous API signatures that result in database calls**. Instead you will need to use
asynchronous APIs. For example, a call to `IQuerySession.Load<MyEntity(id)` will have to be changed to `await IQuerySession.LoadAsync<MyEntity>(id)`.
The only exception is the LINQ `ToList()/ToArray()` type operators that result in making database calls with synchronous
APIs. Due to Npgsql dropping support for sync APIs in Npgsql 10, these APIs will be removed in Marten 9 and throw `NotSupportedException` exceptions asking
you to switch to asynchronous methods instead.

- Nullable Reference Types has been enabled across the entire project which will result in some APIs appearing nullable or non-nullable when they weren't in the past. Please open an issue if you run into incorrect annotations.

- The basic shared dependencies underneath Marten and its partner project [Wolverine](https://wolverinefx.net) were consolidated
for the V8 release into the new, core [JasperFx and JasperFx.Events](https://github.com/jasperfx/jasperfx) libraries. This is
going to cause some changes to your Marten system when you upgrade:

- Some core types like `IEvent` and `StreamAction` moved into the new JasperFx.Events library. Hopefully your IDE can help you change namespace references in your code

- JasperFx subsumed what had been "Oakton" for command line parsing. There are temporarily shims for all the public Oakton types and methods, but from
  this point forward, the core JasperFx library has all the command line parsing and you can pretty well change "Oakton" in your code to "JasperFx"

- The previous "Marten.CommandLine" Nuget was combined into the core Marten library. You will need to remove any explicit references to this Nuget.

- The new projection support in JasperFx.Events no longer uses any code generation for any of the projections. The code generation
for entity types, ancillary document stores, and some internals of the event store still exists unchanged.

- The Open Telemetry span names inside the async daemon do not embed the database identifier in the case of multi-tenancy through separate databases. Instead,
  all projection and subscription activity has the same naming, but the database is a tag on the span if you want to disambiguate the work. 

- If you create a custom implementation of `IProjection` in Marten 8, the projection name is the type name instead of the earlier full name. You may need to override
  the projection name in this case to reflect your older usage.

### Event Sourcing

The projection base classes have minor changes in Marten 8:

- The `SingleStreamProjection` now requires 2 generic type arguments for both the projected document type and the identity type of that document. This compromise was made to better support the increasing widespread usage of strong typed identifiers.

v7: `InvoiceProjection : SingleStreamProjection<Invoice>`

v8: `InvoiceProjection : SingleStreamProjection<Invoice, InvoiceId>`

- Both `SingleStreamProjection` and `MultiStreamProjection` have improved options for writing explicit code for projections for more complex scenarios or if you just prefer that over the conventional `Apply` / `Create` method approach
- `CustomProjection` has been deprecated and marked as `[Obsolete]`! Moreover, it's just a direct subclass of `MultiStreamProjection` now
- There is also an option in `EventProjection` to use explicit code in place of the its conventional usage, and this is the new recommended approach
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/Samples/ConfiguringSessionCreation/Startup.cs#L56-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_addmartenwithcustomsessioncreation' title='Start of snippet'>anchor</a></sup>
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

- Very significant improvements to querying through document child collections by being able to opt into
  JSONPath or containment operator querying in many cases. Early reports suggest an order of magnitude improvement in
  query times. 
- GIST/GIN indexes should be effective with Marten queries again
- The `IMethodCallParser` interface changed slightly, and any custom implementations will have to be adjusted
- Covers significantly more use cases within the LINQ `Where()` filtering
- `Select()` support was widened to include constructor functions

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

- **We Dropped support of .NET Core 3.1 and .NET 5** following the [Official .NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy). That allowed us to benefit fully from recent .NET improvements around asynchronous code, performance etc. Plus made maintenance easier by removing branches of code. If you're using those .NET versions, you need to upgrade to .NET 6 or 7.
- **Upgraded Npgsql version to 7.** If your project uses an explicitly lower version of Npgsql than 7, you'll need to bump it. We didn't face substantial issues this time, so you might not need to do around it, but you can double-check in the [Npgsql 7 release notes](https://www.npgsql.org/doc/release-notes/7.0.html#breaking-changes) for detailed information about breaking changes on their side.
- **Generic `OpenSession` store options (`OpenSession(SessionOptions options)` does not track changes by default.** Previously, it was using [identity map](https://martendb.io/documents/sessions.md#identity-map-mechanics). Other overloads of `OpenSession` didn't change the default behavior but were made obsolete. We encourage using explicit session creation and `LightweightSession` by default, as in the next major version, we plan to do the full switch. Read more about the [Unit of Work mechanics](/documents/sessions.md#unit-of-work-mechanics).
- **Renamed asynchronous session creation to include explicit Serializable name.** `OpenSessionAsync` was misleading, as the intention behind it was to enable proper handling of Postgres' serialized transaction level. Renamed the method to `OpenSerializableSessionAsync` and added explicit methods for session types. Check more in [handling Transaction Isolation Level](/documents/sessions.md#enlisting-in-existing-transactions).
- **Removed obsolete methods marked as to be removed in the previous versions.**:
  - Removed synchronous'BuildProjectionDaemon`from the`IDocumentStore` method. Use the asynchronous version instead.
  - Removed `Schema` from `IDocumentStore`. Use `Storage` instead.
  - Replaced `GroupEventRange` in `IAggregationRuntime` with `Slicer` reference.
  - Removed unused `UseAppendEventForUpdateLock` setting.
  - Removed the `Searchable` method from `MartenRegistry`. Use `Index` instead.
    **[ASP.NET JSON streaming `WriteById`](/documents/aspnetcore.md#single-document) is now using correctly custom `onFoundStatus`.** We had the bug and always used the default status. It's enhancement but also technically a breaking change to the behavior. We also added `onFoundStatus` to other methods, so you could specify, e.g. `201 Created` status for creating a new record.
- **Added [Optimistic concurrency checks](/documents/concurrency.md#optimistic-concurrency) during documents' updates.** Previously, they were only handled when calling the `Store` method; now `Update` uses the same logic.
- **Base state passed as parameter is returned from `AggregateStreamAsync` instead of null when the stream is empty.** `AggregateStreamAsync` allows passing the default state on which we're applying events. When no events were found, we were always returning null. Now we'll return the passed value. It is helpful when you filter events from a certain version or timestamp. It'll also be useful in the future for archiving scenarios
- **Ensured events with both `Create` and `Apply` in stream aggregation were handled only once.** When you defined both Create and Apply methods for the specific event, both methods were called for the single event. That wasn't expected behavior. Now they'll be only handled once.
- **Added missing passing Cancellation Tokens in all async methods in public API.** That ensures that cancellation is handled correctly across the whole codebase. Added the static analysis to ensure we won't miss them in the future.
- **All the Critter Stack dependencies like `Weasel`, `Lamar`, `JasperFx.Core`, `Oakton`, and `JasperFx.CodeGeneration` were bumped to the latest major versions.** If you use them explicitly, you'll need to align the versions.

### Besides that, non-breaking but important changes to upgrade are:

- **Added explicit `LightweightSession` and `IdentitySession` creation methods to `DocumentStore`**. Previously you could create `DirtyTrackedSession` explicitly. Now you can create all types of sessions explicitly. We recommend using them explicitly instead of the generic `OpenSession` method.
- **Renamed aggregations into projections and `SelfAggregate` into `Snapshot` and `LiveStreamAggregation`.** The established terms in the Event Sourcing community are Projection and Snapshot. Even though our naming was more precise on the implementation behind the scenes, it could be confusing. We decided to align it with the common naming and be more explicit about the intention. Old methods were marked as obsolete and will be removed in the next major release.

### Other notable new features:

- **[Added support for reusing Documents in the same async projection batch](/events/projections/event-projections.md#reusing-documents-in-the-same-batch).** By default, Marten does batch to handle multiple events for the projection in one update. When using `EventProjection` and updating data manually using `IDocumentOperations`, this may cause changes made for previous batch items not to be visible. Now you can opt-in for tracking documents by an identity within a batch using the `EnableDocumentTrackingByIdentity` async projection option. Read more in [related docs](/events/projections/event-projections.md#reusing-documents-in-the-same-batch).
- **Enabled the possibility of applying projections with different Conjoined Tenancy scopes for projections.** Enabled global projection for events with a conjoined tenancy style. Read more in [multi-tenancy documentation](/documents/multi-tenancy.md)
- **Added automatic retries when schema updates are running in parallel.** Marten locks the schema update using advisory locks. Previously when acquiring lock failed, then schema update also failed. Now it will be retried, which enables easier parallel automated tests and running schema migration during the startup for the containerized environment.

## Key Changes in 5.0.0

V5 was a much smaller release for Marten than V4, and should require much less effort to move from V4 to V5 as it did from V2/3 to V4.

- The [async daemon](/events/projections/async-daemon) has to be explicitly added with a chained call to `AddAsyncDaemon(mode)`
- The [Marten integration with .Net bootstrapping](/getting-started) now has the ability to split the Marten configuration for testing overrides or modular configuration
- `IInitialData` services are executed within IHost bootstrapping. See [Initial Baseline Data](/documents/initial-data).
- New facility to [apply all detected database changes on application startup](/schema/migrations.html#apply-all-outstanding-changes-upfront).
- Ability to [register multiple Marten document stores in one .Net IHost](/configuration/hostbuilder.html#working-with-multiple-marten-databases)
- The "pre-built code generation" feature had a new, easier to use option in V5 (retired in 9.0 — see [Runtime code generation removed](#runtime-code-generation-removed))
- New ["Optimized Artifact Workflow"](/configuration/optimized_artifact_workflow) option
- Some administrative or diagnostic methods that were previously on `IDocumentStore.Advanced` migrated to database specific access [as shown here](/configuration/multitenancy.html#administering-multiple-databases).

## Key Changes in 4.0.0

V4 was a very large release for Marten, and basically every subsystem was touched at some point. When you are upgrading from V2/3 to V4 -- and even
earlier alphas or RC releases of 4.0 -- you will need to run a [database migration](/schema/migrations) as part of your migration to V4.

Other key, breaking changes:

- All schema management methods, including assertions on the schema, are now asynchronous. We had to do this for Npgsql connection multiplexing.
- The [compiled query](/documents/querying/compiled-queries) syntax changed
- The [event store](/events/) support has quite a few additions
- [Projections](/events/projections/) in Marten have moved to an all new programming model. Some of it is at least similar, but read the documentation on projection types before moving a Marten application over
- The [async daemon](/events/projections/async-daemon) was completely rewritten, and is now about to run in application clusters and handle multi-tenancy
- A few diagnostic methods moved within the API
- Document types need to be public now, and Marten will alert you if document types are not public
- The dynamic code in Marten moved to a runtime code generation model. (Marten 9.0 retired that path entirely — see [Runtime code generation removed](#runtime-code-generation-removed).)
- If an application bootstraps Marten through the `IServiceCollection.AddMarten()` extension methods, the default logging in Marten is through the standard
  `ILogger` of the application
- In order to support more LINQ query permutations, LINQ queries are temporarily not using the GIN indexable operators on documents that have `GinIndexJsonData()` set. Support for this can be tracked [in this GitHub issue](https://github.com/JasperFx/marten/issues/2051)
- PLV8 support is disabled by default and moved to a separate package.
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

- To keep Marten fully rebuilding your schema (so to allow Marten drop tables) set store options to:

```csharp
AutoCreateSchemaObjects = AutoCreate.All
```

- To keep [enum fields](/documents/indexing/duplicated-fields) being stored as `varchar` set store options to:

```csharp
DuplicatedFieldEnumStorage = EnumStorage.AsString;
```

- To keep [duplicated DateTime fields](/documents/indexing/duplicated-fields) being stored as `timestamp with time zone` set store options to:

```csharp
DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime = false;
```
