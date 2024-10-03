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
        ProjectionName = "Distance";
    }

    // Create a new Distance document based on a Travel event
    public Distance Create(Travel travel, IEvent e)
    {
        return new Distance {Id = e.Id, Day = travel.Day, Total = travel.TotalDistance()};
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/event_projections_end_to_end.cs#L161-L177' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_create_in_event_projection' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/event_projections_end_to_end.cs#L92-L102' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuild-single-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Rebuilding a Single Stream <Badge type="tip" text="7.28" />

A long standing request has been to be able to rebuild only a single stream or subset of streams
by stream id (or string key). Marten now has a (admittedly crude) ability to do so with this syntax
on `IDocumentStore`:

<!-- snippet: sample_rebuild_single_stream -->
<a id='snippet-sample_rebuild_single_stream'></a>
```cs
await theStore.Advanced.RebuildSingleStreamAsync<SimpleAggregate>(streamId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/rebuilding_a_single_stream_projection.cs#L29-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuild_single_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
