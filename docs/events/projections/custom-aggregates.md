# Custom Aggregations

Once in awhile users are hitting use cases or desired functionality for aggregation projections that just don't fit in well to our [`AggregateProjection<T>`](/events/projections/aggregate-projections) or [`ViewProjection<TDoc, TId>`](/events/projections/view-projections) models. Not to worry though, because
Marten V5.0 introduces the new `CustomAggregation<T>` base type that will let you define aggregation projections with explicit user code while still taking advantage of some of the parallelization
optimizations that were built for the previous aggregation types running in the [async daemon](/events/projections/async-daemon);

Alright, let's jump right into an example. Two of the drivers for this feature were for aggregations to document types that were [soft-deleted](/documents/deletes.html#soft-deletes) or aggregations where some events should only apply to the aggregate document if the document already existed. To illustrate this with a contrived example, let's say that we've got these event types:

<!-- snippet: sample_custom_aggregate_events -->
<a id='snippet-sample_custom_aggregate_events'></a>
```cs
public class Start{}
public class End{}
public class Restart{}
public class Increment{}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/CustomAggregationTests.cs#L394-L401' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom_aggregate_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And a simple aggregate document type like this:

<!-- snippet: sample_StartAndStopAggregate -->
<a id='snippet-sample_startandstopaggregate'></a>
```cs
public class StartAndStopAggregate : ISoftDeleted
{
    // These are Marten controlled
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public int Count { get; set; }

    public Guid Id { get; set; }

    public void Increment()
    {
        Count++;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/CustomAggregationTests.cs#L374-L392' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_startandstopaggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As you can see, `StartAndStopAggregate` as a `Guid` as its identity and is also [soft-deleted](/documents/deletes.html#soft-deletes) when stored by
Marten by virtue of implementing the `ISoftDeleted` interface.

With all that being done, here's a sample aggregation that inherits from the Marten `Marten.Events.Aggregation.CustomAggregation<TDoc, TId>` base class:

<!-- snippet: sample_custom_aggregate_with_start_and_stop -->
<a id='snippet-sample_custom_aggregate_with_start_and_stop'></a>
```cs
public class StartAndStopProjection: CustomAggregation<StartAndStopAggregate, Guid>
{
    public StartAndStopProjection()
    {
        // I'm telling Marten that events are assigned to the aggregate
        // document by the stream id
        AggregateByStream();

        // This is an optional, but potentially important optimization
        // for the async daemon so that it sets up an allow list
        // of the event types that will be run through this projection
        IncludeType<Start>();
        IncludeType<End>();
        IncludeType<Restart>();
        IncludeType<Increment>();
    }

    public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<StartAndStopAggregate, Guid> slice, CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {

        var aggregate = slice.Aggregate;

        foreach (var data in slice.AllData())
        {
            switch (data)
            {
                case Start:
                    aggregate = new StartAndStopAggregate
                    {
                        // Have to assign the identity ourselves
                        Id = slice.Id
                    };
                    break;
                case Increment when aggregate is { Deleted: false }:
                    // Use explicit code to only apply this event
                    // if the aggregate already exists
                    aggregate.Increment();
                    break;
                case End when aggregate is { Deleted: false }:
                    // This will be a "soft delete" because the aggregate type
                    // implements the IDeleted interface
                    session.Delete(aggregate);
                    aggregate.Deleted = true; // Got to help Marten out a little bit here
                    break;
                case Restart when (aggregate == null || aggregate.Deleted):
                    // Got to "undo" the soft delete status
                    session
                        .UndoDeleteWhere<StartAndStopAggregate>(x => x.Id == slice.Id);
                    break;
            }
        }

        // Apply any updates!
        if (aggregate != null)
        {
            session.Store(aggregate);
        }

        // We didn't do anything that required an asynchronous call
        return new ValueTask();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/CustomAggregationTests.cs#L403-L471' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom_aggregate_with_start_and_stop' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Grouping

All aggregations in Marten come in two parts:

1. *Grouping* incoming events into "slices" of events that should be applied to an aggregate by id
2. *Applying* incoming events from each slice into the identified aggregate

`CustomAggregate` supports aggregating by the stream identity as shown above. You can also use all the same customizable grouping functionality as
the older [ViewProjection](/events/projections/view-projections) subclass.
