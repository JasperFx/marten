# Rebuilding Projections

Projections can be completely rebuilt with the [async daemon](/events/projections/async-daemon) subsystem. Both inline
and asynchronous projections can be rebuilt with the async daemon.

<!-- snippet: sample_using_create_in_event_projection -->
<a id='snippet-sample_using_create_in_event_projection'></a>
```cs
public class DistanceProjection: EventProjection
{
    public DistanceProjection()
    {
        Name = "Distance";
    }

    // Create a new Distance document based on a Travel event
    public Distance Create(Travel travel, IEvent e)
    {
        return new Distance {Id = e.Id, Day = travel.Day, Total = travel.TotalDistance()};
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/EventProjections/event_projections_end_to_end.cs#L146-L162' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_create_in_event_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_rebuild-single-projection -->
<a id='snippet-sample_rebuild-single-projection'></a>
```cs
StoreOptions(x => x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

var agent = await StartDaemon();

// setup test data
await PublishSingleThreaded();

// rebuild projection `Distance`
await agent.RebuildProjectionAsync("Distance", CancellationToken.None);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/EventProjections/event_projections_end_to_end.cs#L77-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuild-single-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Optimized Projection Rebuilds <Badge type="tip" text="7.30" />

::: warning
Sorry, but this feature is pretty limited right now. This optimization is only today usable if there is exactly *one*
single stream projection using any given event stream. If you have two or more single stream projection views for the same
events -- which is a perfectly valid use case and not uncommon -- the optimized rebuilds will not result in correct behavior.

The current limitations of this optimized rebuild feature are a known area for improvement. 
The MartenDB creator has indicated that this functionality may either be thoroughly re-addressed and fixed within MartenDB,
or potentially externalized into a future commercial add-on product (e.g., "CritterWatch") designed to provide more advanced event store tooling.
Developers should be aware that while this feature offers performance benefits in specific, limited scenarios,
its broader application for multiple single stream projections is currently not supported and may be subject to future changes in its implementation or availability.
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
