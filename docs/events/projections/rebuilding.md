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

This feature in its 7.* form was not successful and is not supported *yet* in Marten 8.0. The current plan
is to rebuild an improved version of this application in the forthcoming commercial "CritterWatch" tool.

## Rebuilding a Single Stream <Badge type="tip" text="7.28" />

A long standing request has been to be able to rebuild only a single stream or subset of streams
by stream id (or string key). Marten now has a (admittedly crude) ability to do so with this syntax
on `IDocumentStore`:

<!-- snippet: sample_rebuild_single_stream -->
<a id='snippet-sample_rebuild_single_stream'></a>
```cs
await theStore.Advanced.RebuildSingleStreamAsync<SimpleAggregate>(streamId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/rebuilding_a_single_stream_projection.cs#L31-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuild_single_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
