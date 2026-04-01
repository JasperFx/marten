# Rebuilding Projections

Projections can be completely rebuilt with the [async daemon](/events/projections/async-daemon) subsystem. Both inline
and asynchronous projections can be rebuilt with the async daemon.

Rebuilds can be performed via the [command line](/configuration/cli) or in code as below.

For example, if we have this projection:

<!-- snippet: sample_rebuild-shop_projection -->
<a id='snippet-sample_rebuild-shop_projection'></a>
```cs
public class ShopProjection: SingleStreamProjection<Guid, Shop>
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

## Optimized Projection Rebuilds <Badge type="tip" text="7.30" />

::: tip
This optimization will be turned on by default in Marten 8, but we didn't want to force anyone using Marten 7 to have
to upgrade their database without the explicit opt in configuration.
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
