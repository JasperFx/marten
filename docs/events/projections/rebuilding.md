# Rebuilding Projections

Projections can be completely rebuilt with the [async daemon](/events/projections/async-daemon) subsystem. Both inline
and asynchronous projections can be rebuilt with the async daemon.

Rebuilds can be performed via the [command line](/configuration/cli) or in code as below.

For example, if we have this projection:

<!-- snippet: sample_rebuild-shop_projection -->
<a id='snippet-sample_rebuild-shop_projection'></a>
```cs
public partial class ShopProjection: SingleStreamProjection<Guid, Shop>
{
    public ShopProjection()
    {
        Name = "Shop";
    }

    // Create a new Shop document based on a CreateShop event
    public Shop Create(ShopCreated @event)
    {
        return new Shop(@event.Id, @event.Items);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RebuildRunner.cs#L12-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuild-shop_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

We can rebuild it by calling `RebuildProjectionAsync` against an async daemon:

<!-- snippet: sample_rebuild-single-projection -->
<a id='snippet-sample_rebuild-single-projection'></a>
```cs
private IDocumentStore _store;

public RebuildRunner(IDocumentStore store)
{
    _store = store;
}

public async Task RunRebuildAsync()
{
    using var daemon = await _store.BuildProjectionDaemonAsync();

    await daemon.RebuildProjectionAsync("Shop", CancellationToken.None);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RebuildRunner.cs#L30-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuild-single-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Capping Rebuild Concurrency <Badge type="tip" text="9.13" />

Rebuilding fans out one rebuild "cell" per (projection × tenant/shard). On a wide store — many projections, or
`UseTenantPartitionedEvents` with many tenants — an unbounded fan-out can exhaust the database connection pool and
thrash the buffer cache. Marten caps the number of cells that run concurrently against one database:

```cs
// Explicit cap
opts.Projections.MaxConcurrentRebuildsPerDatabase = 6;
```

If you don't set it, Marten derives a conservative default from the Npgsql connection pool size:
`max(1, MaxPoolSize / 8)` — e.g. a 100-connection pool (the Npgsql default) allows 12 concurrent rebuild cells, a
20-connection pool allows 2. The fraction leaves headroom for application traffic during rebuild windows. Setting
the knob to zero or a negative number opts back into the historical unbounded fan-out.

Two things to know about the shape of the throttle:

- **It caps rebuild only.** Continuous catch-up is governed separately by
  `opts.Projections.MaxConcurrentEventLoadsPerDatabase` and `opts.Projections.MaxConcurrentBatchWritesPerDatabase`
  (both default 4) — see [Daemon Connection Governors](/events/projections/async-daemon#daemon-connection-governors).
- **It's a two-layer model.** The cap bounds how many cells run at once; each cell still uses its own internal
  slice workers while it runs. A cap of 4 therefore means "4 rebuilding projections/tenants at a time," not 4
  concurrent database operations.

For one-off operational rebuilds, the `--max-concurrent` flag on the [command line](/configuration/cli) overrides
the configured value for that run:

```bash
dotnet run -- projections rebuild --max-concurrent 2
```

The effective cap is surfaced to monitoring tools through the store's usage descriptor
(`EventStoreUsage.MaxConcurrentRebuildsPerDatabase`), so tools like CritterWatch can size their own rebuild
orchestration to match.

### Load-test evidence for the defaults

The `max(1, MaxPoolSize / 8)` cap and the load/write governor default of 4 are confirmed by the
`rebuildload` scenario in the `Marten.ScaleTesting` harness (marten#4884), which sweeps the three knobs
against many partitioned tenants rebuilding concurrently and samples `pg_stat_activity` +
`mt_event_progression` contention per configuration. The governing observation is that peak database
connections track the outer cap directly — **peak connections ≈ min(cap, projections) + 1** — so the cap,
not the inner slice workers, is the dominant connection driver, and the `MaxPoolSize / 8` fraction keeps peak
usage comfortably inside pool headroom (e.g. peak 9 against a 100-connection pool at cap 12). Wall-clock gains
flatten past a handful of concurrent cells (diminishing returns), and no `mt_event_progression` waiters appear
across the swept caps, so raising the cap trades pool headroom for little rebuild speedup once past ~4.
`EnableExtendedProgressionTracking` adds no measurable rebuild overhead at the scales tested. The
`rebuildload --databases N` sharded sweep confirms the cap is a **per-database** governor: when N
pooled shard databases rebuild concurrently, each shard's peak tracks the cap independently, so the
cluster footprint stays O(databases × per-database cap) rather than a shared blowup, with no
`mt_event_progression` contention across shards. Re-run `rebuildload` at your production pool size and
event volume before deviating from the defaults.

## Cancelling a Rebuild <Badge type="tip" text="9.13" />

`RebuildProjectionAsync` overloads accept a `CancellationToken`, including the per-tenant overload
(`RebuildProjectionAsync(name, tenantId, token)`) used with per-tenant event partitioning. Cancellation honors
this contract:

- Cancelling an in-flight rebuild leaves the cell's `mt_event_progression` row in a consistent state — either
  unchanged from before the rebuild or at the actual partial position the rebuild reached. Never a torn,
  in-between state.
- A subsequent `RebuildProjectionAsync` on the same (projection, tenant) cell completes successfully with no
  manual intervention — a rebuild always starts by resetting the cell, so a cancelled rebuild can simply be
  retried.

This is what makes it safe for operational tooling to expose a "cancel" affordance on long-running rebuilds.

## Bulk Copy Rebuild Writes <Badge type="tip" text="experimental" />

A projection rebuild has a property that continuous catch-up does not: after the projection's
existing rows are torn down, the entire rebuild write path is **insert-only** — the rebuild is
authoritative by definition, so there is no need for the `UPSERT` / `ON CONFLICT` machinery the
continuous path uses. PostgreSQL's binary `COPY` protocol is typically several times faster than
per-row `INSERT` for bulk loads, and Marten already uses it for `IDocumentStore.BulkInsertAsync`.

Opt in with:

```cs
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // When a rebuild batch's document writes are pure inserts, flush them
    // through PostgreSQL binary COPY instead of the per-row INSERT path.
    opts.Projections.RebuildWithBulkCopy = true;
});
```

When enabled, a **rebuild** batch buffers document inserts and flushes them through the same
`COPY` (BulkWriter) machinery `BulkInsertAsync` uses — id assignment, `tenant_id`, the `data`
column, and every metadata column (version, last-modified, .NET type, soft-delete flags,
duplicated fields) are written exactly as the per-row path writes them, and the `COPY` runs
inside the batch's existing transaction so a failed rebuild cannot leak partially-copied rows.

This targets **event-to-document (`EventProjection`) rebuilds** — a projection whose handlers call
`IDocumentOperations.Insert(...)`, producing one new document per event. That shape is genuinely
insert-only across event pages, so it is safe today.

The dispatch degrades gracefully and is safe to leave on:

- Only **rebuild** batches are affected. Continuous (catch-up) projection execution always keeps
  the per-row `UPSERT` path — there is no behavior change there.
- If any non-insert document operation (update, upsert, patch, delete, ad-hoc SQL) shows up in the
  same batch, the buffered inserts drain back onto the ordinary per-row command path in their
  original order and the batch executes exactly as it would without the flag. A mixed batch simply
  doesn't get the `COPY` win.
- Aggregation / single-stream-snapshot projections re-store the same aggregate id across event
  pages during a rebuild; that is an `UPSERT`, not a pure insert, so those rebuilds continue to use
  the per-row path even with the flag on. Extending the `COPY` win to aggregation rebuilds composes
  with the deferred-flush work tracked separately.

The flag defaults to `false`.

## Optimized Projection Rebuilds <Badge type="tip" text="7.30" />

::: tip
This optimization must be explicitly opted into via `opts.Events.UseOptimizedProjectionRebuilds = true`. It is not enabled by default because it requires a database schema migration for users upgrading from earlier versions.
:::

::: warning
Sorry, but this feature is pretty limited right now. This optimization is only today usable if there is exactly *one*
single stream projection using any given event stream. If you have two or more single stream projection views for the same
events -- which is a perfectly valid use case and not uncommon -- the optimized rebuilds will not result in correct behavior.
:::

Marten can optimize the projection rebuilds of single stream projections by opting into this flag in your configuration:

<!-- snippet: sample_turn_on_optimizations_for_rebuilding -->
<a id='snippet-sample_turn_on_optimizations_for_rebuilding'></a>
```cs
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // Opts into a mode where Marten is able to rebuild single // [!code ++]
    // stream projections faster by building one stream at a time // [!code ++]
    // Does require new table migrations for Marten 7 users though // [!code ++]
    opts.Events.UseOptimizedProjectionRebuilds = true; // [!code ++]
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/Optimizations.cs#L61-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_turn_on_optimizations_for_rebuilding' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In this mode, Marten will rebuild single stream projection documents stream by stream in the reverse order that the 
streams were last modified. This was conceived of as being combined with the [`FetchForWriting()`](/scenarios/command_handler_workflow.html#fetchforwriting) usage with asynchronous
single stream projections for zero downtime deployments while trying to create less load on the database than the original
"left fold" / "from zero" rebuild would be. 

## Blue/Green Deployments with Projection Versioning

When deploying projection changes to production without downtime, you can use projection
versioning to run old and new projection versions in parallel:

1. **Increment `ProjectionVersion`** on your projection class to create a new version that
   writes to separate database tables from the previous version
2. **Use Async lifecycle** for the new version so it can "catch up" to the current event
   sequence while the old version continues serving requests
3. **Deploy new nodes** ("green") running the updated code alongside existing nodes ("blue").
   The green nodes build the new projection version while blue nodes continue serving traffic
4. **Switch traffic** to the green nodes once the new projection has caught up

The `FetchForWriting()` API handles this transparently -- it provides strong consistency
regardless of the underlying projection lifecycle, so command handlers work correctly during
the transition period without code changes.

When using Wolverine's managed event subscription distribution
(`UseWolverineManagedEventSubscriptionDistribution = true`), projection shards are
automatically distributed across all nodes in the cluster, enabling parallel execution
of old and new projection versions.

For a deeper discussion of this deployment strategy, see
[Projections, Consistency Models, and Zero Downtime Deployments](https://jeremydmiller.com/2025/03/26/projections-consistency-models-and-zero-downtime-deployments-with-the-critter-stack/).

### Suppressing Side Effects During the Blue/Green Warm-up

A projection that raises [side effects](/events/projections/side-effects) (appended events or
published messages via `RaiseSideEffects()`) presents a problem for blue/green versioning: when the
new version (`V{n+1}`) starts as `Async` it catches up over the **entire** event history — including
the events the previous version already processed and already fired side effects for. Without a
guard, deploying a new version re-emits every one of those side effects.

Opt into `GateSideEffectsBehindPriorVersion` to prevent that:

```cs
public class TripProjection : SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        Version = 3;

        // Only fire side effects for events the prior version (V2) never processed
        GateSideEffectsBehindPriorVersion = true;
    }

    // Apply(...) methods and RaiseSideEffects(...) as usual
}
```

When the flag is on and a new version starts behind the highest prior version's persisted
progression mark `N`, the daemon does the catch-up in two phases:

1. **Warm-up** — replay `(current, N]` in **Rebuild** mode, so `RaiseSideEffects()` is suppressed
   while the new version's documents are brought up to the same point the prior version reached.
2. **Continuous** — hand off to normal continuous execution from `N`, so side effects fire only for
   events past `N` — the ones the prior version never saw.

Behavioral notes:

- **Resume after interruption** — the trigger is *own progress `< N`*, not *own progress `== 0`*. If
  the warm-up is interrupted (a crash or restart at `M < N`), the next start resumes the suppressed
  replay over `(M, N]` rather than re-emitting side effects from the beginning.
- **Failed warm-up leaves the shard paused** — if the warm-up replay throws, the shard is left
  `Paused` with the exception attached and continuous execution does **not** start (so no side
  effects fire over history the prior version already covered). Restarting the shard resumes the
  suppressed warm-up from its persisted progress.
- **Incompatible with `SubscribeFromPresent`** — subscribing from "present" deliberately ignores
  persisted progression, so there is no prior mark to gate against. The gate is skipped and a warning
  is logged.
- **No-ops when not needed** — the gate never adds a warm-up phase for `Version == 1`, when the flag
  is off, or when the new version's own progress is already at/past the prior mark (a plain resume).
- **Accepted overlap window** — `N` is snapshotted when the new version starts. If an old version is
  still running and advances past `N` afterward, the new version can re-emit side effects for that
  `(N, old_final]` overlap. Stop the old version before (or as) the new one starts to avoid the
  window; fully coordinated drain-and-handoff is a separate concern.

## Rebuilding a Single Stream <Badge type="tip" text="7.28" />

A long standing request has been to be able to rebuild only a single stream or subset of streams
by stream id (or string key). Marten now has a (admittedly crude) ability to do so with this syntax
on `IDocumentStore`:

<!-- snippet: sample_rebuild_single_stream -->
<a id='snippet-sample_rebuild_single_stream'></a>
```cs
await theStore.Advanced.RebuildSingleStreamAsync<SimpleAggregate>(streamId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/rebuilding_a_single_stream_projection.cs#L32-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuild_single_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
