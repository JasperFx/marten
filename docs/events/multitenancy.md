# Event Store Multi-Tenancy

The event store feature in Marten supports an opt-in multi-tenancy model that captures
events by the current tenant. Use this syntax to specify that:

<!-- snippet: sample_making_the_events_multi_tenanted -->
<a id='snippet-sample_making_the_events_multi_tenanted'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // And that's all it takes, the events are now multi-tenanted
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L236-L246' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_making_the_events_multi_tenanted' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Global Streams & Projections Within Multi-Tenancy <Badge type="tip" text="8.5" />

Document storage allows you to mix conjoined- and single-tenanted documents in one database. You can now do the same
thing with event storage and projected aggregate documents from `SingleStreamProjection<TDoc, TId>` projections.

Let's say that you have a document (cut us some slack, this came from testing) called `SpecialCounter` that is aggregated from events
in your system that otherwise has a conjoined tenancy model for the event store, but `SpecialCounter` should
be global within your system. 

Let's start with a possible implementation of a single stream projection:

<!-- snippet: sample_specialcounterprojection -->
<a id='snippet-sample_specialcounterprojection'></a>
```cs
public partial class SpecialCounterProjection: SingleStreamProjection<SpecialCounter, Guid>
{
    public void Apply(SpecialCounter c, SpecialA _) => c.ACount++;
    public void Apply(SpecialCounter c, SpecialB _) => c.BCount++;
    public void Apply(SpecialCounter c, SpecialC _) => c.CCount++;
    public void Apply(SpecialCounter c, SpecialD _) => c.DCount++;

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs#L395-L406' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_specialcounterprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or this equivalent, but see how I'm explicitly registering event types, because that's going to be important:

<!-- snippet: sample_specialcounterprojection2 -->
<a id='snippet-sample_specialcounterprojection2'></a>
```cs
public partial class SpecialCounterProjection2: SingleStreamProjection<SpecialCounter, Guid>
{
    public SpecialCounterProjection2()
    {
        // This is normally just an optimization for the async daemon,
        // but as a "global" projection, this also helps Marten
        // "know" that all events of these types should always be captured
        // to the default tenant id
        IncludeType<SpecialA>();
        IncludeType<SpecialB>();
        IncludeType<SpecialC>();
        IncludeType<SpecialD>();
    }

    public void Apply(SpecialCounter c, SpecialA _) => c.ACount++;
    public void Apply(SpecialCounter c, SpecialB _) => c.BCount++;
    public void Apply(SpecialCounter c, SpecialC _) => c.CCount++;
    public void Apply(SpecialCounter c, SpecialD _) => c.DCount++;

    public override SpecialCounter Evolve(SpecialCounter snapshot, Guid id, IEvent e)
    {
        snapshot ??= new SpecialCounter { Id = id };
        switch (e.Data)
        {
            case SpecialA _:
                snapshot.ACount++;
                break;
            case SpecialB _:
                snapshot.BCount++;
                break;
            case SpecialC _:
                snapshot.CCount++;
                break;
            case SpecialD _:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs#L410-L454' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_specialcounterprojection2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And finally, let's register our projection within our application's bootstrapping:

<!-- snippet: sample_bootstrapping_with_global_projection -->
<a id='snippet-sample_bootstrapping_with_global_projection'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // The event store has conjoined tenancy...
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;

    // But we want any events appended to a stream that is related
    // to a SpecialCounter to be single or global tenanted
    // And this works with any ProjectionLifecycle
    opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), ProjectionLifecycle.Inline);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs#L360-L376' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bootstrapping_with_global_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The impact of this global registration is that any events appended to a stream with an aggregate type of `SpecialCounter`
or really any events at all of the types known to be included in the globally registered single stream projection will
be appended as the default tenant id _no matter what the session's tenant id is_. There's a couple implications here:

1. The event types of a globally applied projection should not be used against other types of streams
2. Marten "corrects" the tenant id applied to events from globally projected aggregates regardless of how the events are appended or how the session was created
3. Marten automatically marks the storage for the aggregate type as single tenanted
4. Live, Async, or Inline projections have all been tested with this functionality
5. `AppendOptimistic()` and `AppendPessimistic()` do not work (yet) with this setting, but you should probably
   be using `FetchForWriting()` instead anyway. 

## Per-Tenant Event Partitioning <Badge type="tip" text="9.4" />

::: tip
This is an advanced, opt-in option aimed at large multi-tenanted event stores where a single, shared
event store becomes a scalability bottleneck. It builds on the conjoined event tenancy described above
by physically isolating each tenant's events and giving the async daemon a per-tenant view of progress.
See [JasperFx/marten#4596](https://github.com/JasperFx/marten/issues/4596) and
[CritterStack #209](https://github.com/JasperFx/CritterWatch/issues/209) for the full design.
:::

For systems with many tenants and very high event volumes, you can opt into **per-tenant event partitioning**.
This layers native PostgreSQL LIST partitioning by `tenant_id` on top of the conjoined event tenancy model so
that each tenant's events and streams live in their own physical partitions, get their own event sequence, and
are tracked independently by the asynchronous projection daemon:

```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Per-tenant partitioning requires conjoined event tenancy
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;

    // Per-tenant partitioning only supports the "quick" append modes
    opts.Events.AppendMode = EventAppendMode.Quick;

    // Opt into per-tenant event partitioning
    opts.Events.UseTenantPartitionedEvents = true;
});
```

When `UseTenantPartitionedEvents` is enabled, Marten:

* **Partitions `mt_events` and `mt_streams` by `tenant_id`** using native PostgreSQL LIST partitioning. This
  reuses the same managed-partition machinery (the `mt_tenant_partitions` lookup table) as
  [document partitioning](/configuration/multitenancy#sharded-multi-tenancy-with-database-pooling), but opting
  into per-tenant _events_ does not implicitly partition your multi-tenanted document tables.
* **Gives each tenant its own event sequence** (`mt_events_sequence_{tenant_suffix}`) instead of a single global
  sequence, so high-volume tenants no longer contend on one shared sequence.
* **Keys `mt_event_progression` by `(name, tenant_id)`**, so projection progress is tracked per tenant rather than
  for the store as a whole.
* **Runs the async daemon with a vectorized per-tenant high-water mark** — one query per database reports the high-water
  position for every active tenant in a single round trip — plus **per-tenant rebuild isolation**, so a projection can
  be rebuilt for a single tenant without tearing down or replaying every other tenant's progress.

### What You Get Automatically <Badge type="tip" text="9.13" />

`UseTenantPartitionedEvents` plus your tenancy choice is the whole opt-in — everything below is derived from that
combination with no further configuration:

* **One async daemon agent per (database, tenant).** The daemon runs each async projection or subscription
  independently per tenant, so a lagging or rebuilding tenant never stalls its neighbors. This applies both to
  Marten's own daemon and to [Wolverine-managed projection distribution](https://wolverinefx.net/guide/durability/marten/distribution.html),
  which detects a tenant-partitioned store and automatically fans its distributed agents out per tenant across the
  cluster.
* **Tenant discovery on a plain single database.** With nothing but `opts.Connection(...)`, Marten quietly swaps in
  a tenancy that reads the registered tenant list from the `mt_tenant_partitions` table, so per-tenant agent
  distribution always has the current tenant set to fan out to — tenants added or removed at runtime converge
  without a restart.
* **Database-affine agent placement on multi-database tenancies.** When the store spans multiple databases (e.g.
  [sharded multi-tenancy with database pooling](/configuration/multitenancy#sharded-multi-tenancy-with-database-pooling)),
  distributed hosts group all of one database's agents on the same node, keeping the connection count per database
  flat instead of every node holding connections to every database.
* **Daemon connection governors.** The running daemon caps concurrent event loads and concurrent batch writes at 4
  per database by default, so the connection footprint stays _O(databases)_ rather than growing with
  (projections × tenants). See [Daemon Connection Governors](/events/projections/async-daemon#daemon-connection-governors).
* **A pool-derived rebuild cap.** Projection rebuilds fan out one cell per (projection × tenant), capped by default
  at `max(1, MaxPoolSize / 8)` concurrent cells per database. See
  [Capping Rebuild Concurrency](/events/projections/rebuilding#capping-rebuild-concurrency).
* **Managed partition bookkeeping.** The `mt_tenant_partitions` lookup table and its management machinery are
  created automatically — you do not need to also opt into
  `Policies.PartitionMultiTenantedDocumentsUsingMartenManagement()` (though the two compose if you want partitioned
  document tables too).

### Constraints

Per-tenant partitioning is validated at `DocumentStore` construction. The following combinations throw immediately
rather than failing opaquely later:

* **Requires `TenancyStyle.Conjoined`.** There is nothing to partition by when every event lives in the default tenant.
* **Requires a "quick" append mode** (`EventAppendMode.Quick` or `EventAppendMode.QuickWithServerTimestamps`). The
  per-tenant sequence pick is wired into the quick-append code path only; `EventAppendMode.Rich` assigns sequences
  ahead of time from a shared reader and is explicitly out of scope. See
  ["Rich" vs "Quick" Appends](/events/appending#rich-vs-quick-appends).
* **Cannot currently be combined with `UseArchivedStreamPartitioning`.** Sub-partitioning the event tables by both
  `tenant_id` and `is_archived` is a planned follow-up; pick one for now.

### Registering Tenants

As with document-level managed partitioning, a tenant's partitions must exist before its events can be appended.
Register tenants through the admin API:

```cs
await store.Advanced.AddMartenManagedTenantsAsync(
    cancellationToken,
    "tenant-a", "tenant-b", "tenant-c");
```

This creates the LIST partitions (and per-tenant sequence) for each tenant across the partitioned event tables. When
rebuilding a projection across every tenant, the daemon discovers the full set of registered tenants from the
`mt_tenant_partitions` table and fans out into independent per-tenant rebuilds.

::: tip
Per-tenant event partitioning composes with the
[Sharded Multi-Tenancy with Database Pooling](/configuration/multitenancy#sharded-multi-tenancy-with-database-pooling)
model: sharding distributes tenants across a pool of databases, and per-tenant partitioning physically isolates each
tenant's events _within_ whichever database hosts that tenant.
:::

### Global Projections

Global projections registered with `AddGlobalProjection` (described earlier on this page) route their aggregate's
events to the default tenant slot (`*DEFAULT*`) so that every tenant's contribution lands in one canonical,
single-tenanted timeline. That sentinel value contains characters that are not legal in PostgreSQL identifiers, so it
can never be a partition-table _suffix_ — but a LIST partition _value_ can be any string. Whenever a store has global
aggregates registered, `AddMartenManagedTenantsAsync` automatically provisions a partition for the `*DEFAULT*` tenant
value using the reserved suffix `__default__` (`mt_events___default__`, `mt_streams___default__`, its own
`mt_events_sequence___default__`, and so on) alongside the tenants you register. No extra registration call is
needed. Two things to be aware of:

* The reserved suffix `__default__` is rejected if you try to claim it for a regular tenant.
* The `*DEFAULT*` slot appears in the `mt_tenant_partitions` registry — and therefore in tenant listings derived
  from it — like any other tenant.

### Dropping Tenants

Both routes that remove a tenant under `UseTenantPartitionedEvents` clean up the full
per-tenant footprint -- partition tables, the freestanding `mt_events_sequence_{tenantId}`
sequence, and the per-tenant `mt_event_progression` rows (one per projection's per-tenant
catch-up plus the `HighWaterMark:{tenantId}` row):

```cs
// Wipe all data for a tenant, keep the partition registered (re-seeding works after this)
await store.Advanced.DeleteAllTenantDataAsync("tenant-a", cancellationToken);

// Remove the tenants entirely (drops their partitions + cleanup)
await store.Advanced.RemoveMartenManagedTenantsAsync(
    new[] { "tenant-b", "tenant-c" }, cancellationToken);
```

Store-global progression rows (the `HighWaterMark` constant, `MyProjection:All` without a
tenant suffix) are intentionally preserved by the per-tenant cleanup -- they belong to
the store as a whole. Other tenants' partitions, sequences, and progression rows are
untouched.

The cleanup identifies per-tenant `mt_event_progression` rows by parsing the
`ShardName` grammar rather than pattern-matching the name, so a projection whose name
happens to end with a tenant id is never mistakenly deleted (#4683).

#### Removing tenants at runtime <Badge type="tip" text="9.13" />

Under `MultiTenantedWithShardedDatabases()`, removing or disabling a tenant is honored by an
**already-running** store — no process restart is required. Removal shrinks the store's usage
descriptor (`DescribeDatabasesAsync`) immediately, and a running async daemon **retires that
tenant's per-tenant projection agents on its next leadership cycle** (the coordinator re-expands
each shard's agent set from that shard's own tenant registry and reaps the agents whose tenant is
gone). The surviving tenants keep processing on the same shard daemon, and no further progression
rows are written for the departed tenant.

```cs
// Destructive removal on the tenant's shard: unassigns the tenant, drops its partitions,
// its per-tenant mt_events_sequence, and its per-tenant mt_event_progression rows. The
// running daemon reaps the tenant's agents; a later re-add starts the tenant fresh (a new
// per-tenant sequence starting at 1, no surviving projection state).
await store.Advanced.RemoveTenantFromShardAsync("tenant-b", cancellationToken);

// Re-adding the same tenant later provisions fresh partitions + sequence, and the running
// daemon starts a new agent for it that catches up from the tenant's new (empty) baseline.
await store.Advanced.AddTenantToShardAsync("tenant-b", cancellationToken);
```

::: tip
`RemoveTenantFromShardAsync` is **destructive** — it drops the tenant's shard-side data. For a
**non-destructive** soft-delete that only hides the tenant from the usage descriptor (its
partitions, per-tenant sequence, and registry rows are all retained, and re-enabling restores it
in place), use the sharded tenancy's `DisableTenantAsync` / `EnableTenantAsync` instead:

```cs
var tenancy = (IDynamicTenantSource<string>)store.Options.Tenancy;
await tenancy.DisableTenantAsync("tenant-b");   // descriptor shrinks; shard data retained
await tenancy.EnableTenantAsync("tenant-b");    // restored in place, no re-seeding needed
```
:::

### Migrating an Existing Conjoined Store

::: warning
The migration is **offline-first**: take source writes offline for the migration window. During the
migration both the source and target event tables exist side by side, so plan for roughly 2x the current
event-store disk usage — and take a backup first.
:::

There is one canonical path for moving an existing conjoined event store onto per-tenant partitioning:
`ConjoinedToPartitionedMigration`, driven per tenant by the
[sequence-preserving streaming bulk import](/events/bulk-appending#preserving-source-sequence-numbers).
Point it at the existing store (the source) and a store configured with `UseTenantPartitionedEvents = true`
in a **different schema or database** (the target — the source tables are never touched, so rolling back is
simply "keep using the source"):

```cs
var migration = new ConjoinedToPartitionedMigration(sourceStore, targetStore)
{
    // Optional: rows per COPY batch (default 1000)
    BatchSize = 5000,

    // Optional: migrate only a subset of tenants (default: every tenant found in the source)
    TenantIds = new[] { "tenant-a", "tenant-b" }
};

// Phase 1 — the dry run: per-tenant inventory (event count, stream count, max seq_id)
// plus which tenants a resumed run would skip. Moves no data.
var plan = await migration.BuildPlanAsync(cancellationToken);

// Phase 2 — per-tenant copy: registers each tenant's partitions on the target, streams its
// events across with their original seq_ids preserved, verifies row counts, and records
// completion in the target's mt_tenant_migration_log table.
var result = await migration.ExecuteAsync(cancellationToken);
```

The migration's **data policy is to never renumber historical events**. Every event keeps its original
`seq_id` — per-tenant gaps are expected, because the conjoined source interleaved all tenants on one global
sequence — so anything that captured a sequence position (progression rows, downstream warehouses, audit
logs, external integrations) stays valid. Instead, for each tenant the migration:

* advances the tenant's own `mt_events_sequence_{suffix}` past its imported maximum, so the first live
  append after cut-over works on the first try (no primary-key or sequence collisions);
* seeds the tenant's `HighWaterMark:{tenantId}` progression row at that maximum, so high-water detection
  starts above the gappy imported history;
* carries the `is_archived` flag across for both streams and events.

Tenants are migrated **one at a time, each in a single transaction**. A failure rolls the in-flight tenant
back cleanly; re-running `ExecuteAsync` skips tenants already recorded as completed in
`mt_tenant_migration_log` and retries the failed one. Inline projection documents are _not_ migrated —
rebuild projections on the target after the copy (they replay from the migrated events).
