# Explicit Aggregations

::: warning
The recipe in this documentation section is perfect for complicated projection lifecycles, but does not always play
nicely with Marten's `AggregateStreamAsync()` API. We recommend using `FetchLatest()` with this projection recipe
for read only access.
:::

The original concept for Marten projections was the conventional method model (`Apply()` / `Create()` / `ShouldDelete()` methods), but we
quickly found out that the workflow generated from these methods just isn't sufficient for many user needs. At the same time,
other users just prefer explicit code anyway, so Marten provides the `CustomProjection<TDoc, TId>` base class as a way to 
configure custom projections that use explicit code for the actual work of building projected, aggregate documents from
raw events.

Alright, let's jump right into an example. Two of the drivers for this feature were for aggregations to document types that were [soft-deleted](/documents/deletes.html#soft-deletes) or aggregations where some events should only apply to the aggregate document if the document already existed. To illustrate this with a contrived example, let's say that we've got these event types:

<!-- snippet: sample_custom_aggregate_events -->
<a id='snippet-sample_custom_aggregate_events'></a>
```cs
public class Start
{
}

public class End
{
}

public class Restart
{
}

public class Increment
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/explicit_code_for_aggregation_logic.cs#L563-L581' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom_aggregate_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And a simple aggregate document type like this:

<!-- snippet: sample_StartAndStopAggregate -->
<a id='snippet-sample_startandstopaggregate'></a>
```cs
public class StartAndStopAggregate: ISoftDeleted
{
    public int Count { get; set; }

    public Guid Id { get; set; }

    // These are Marten controlled
    public bool Deleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public void Increment()
    {
        Count++;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/explicit_code_for_aggregation_logic.cs#L543-L561' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_startandstopaggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As you can see, `StartAndStopAggregate` as a `Guid` as its identity and is also [soft-deleted](/documents/deletes.html#soft-deletes) when stored by
Marten by virtue of implementing the `ISoftDeleted` interface.

With all that being done, here's a sample aggregation that inherits from the Marten `Marten.Events.Aggregation.CustomAggregation<TDoc, TId>` base class:

<!-- snippet: sample_custom_aggregate_with_start_and_stop -->
<a id='snippet-sample_custom_aggregate_with_start_and_stop'></a>
```cs
public class StartAndStopProjection: SingleStreamProjection<StartAndStopAggregate, Guid>
{
    public StartAndStopProjection()
    {
        // This is an optional, but potentially important optimization
        // for the async daemon so that it sets up an allow list
        // of the event types that will be run through this projection
        IncludeType<Start>();
        IncludeType<End>();
        IncludeType<Restart>();
        IncludeType<Increment>();
    }

    public override (StartAndStopAggregate?, ActionType) DetermineAction(StartAndStopAggregate? snapshot, Guid identity,
        IReadOnlyList<IEvent> events)
    {
        var actionType = ActionType.Store;

        if (snapshot == null && events.HasNoEventsOfType<Start>())
        {
            return (snapshot, ActionType.Nothing);
        }

        var eventData = events.ToQueueOfEventData();
        while (eventData.Any())
        {
            var data = eventData.Dequeue();
            switch (data)
            {
                case Start:
                    snapshot = new StartAndStopAggregate
                    {
                        // Have to assign the identity ourselves
                        Id = identity
                    };
                    break;

                case Increment when snapshot is { Deleted: false }:

                    if (actionType == ActionType.StoreThenSoftDelete) continue;

                    // Use explicit code to only apply this event
                    // if the snapshot already exists
                    snapshot.Increment();
                    break;

                case End when snapshot is { Deleted: false }:
                    // This will be a "soft delete" because the snapshot type
                    // implements the IDeleted interface
                    snapshot.Deleted = true;
                    actionType = ActionType.StoreThenSoftDelete;
                    break;

                case Restart when snapshot == null || snapshot.Deleted:
                    // Got to "undo" the soft delete status
                    actionType = ActionType.UnDeleteAndStore;
                    snapshot.Deleted = false;
                    break;
            }
        }

        return (snapshot, actionType);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/explicit_code_for_aggregation_logic.cs#L583-L651' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_custom_aggregate_with_start_and_stop' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Custom Grouping

All aggregations in Marten come in two parts:

1. _Grouping_ incoming events into "slices" of events that should be applied to an aggregate by id
2. _Applying_ incoming events from each slice into the identified aggregate

`CustomAggregate` supports aggregating by the stream identity as shown above. You can also use all the same customizable grouping functionality as
the older [MultiStreamProjection](/events/projections/multi-stream-projections) subclass.

## Simple Workflows <Badge type="tip" text="7.28" />

The base class can be used for strictly live aggregations. If all you're doing is using this
mechanism for `Live` aggregation, or have a simple workflow where the aggregate is always
going to be built strictly from the event data, you can override _only_ the `Apply()` method 
as shown below:

<!-- snippet: sample_using_simple_explicit_code_for_live_aggregation -->
<a id='snippet-sample_using_simple_explicit_code_for_live_aggregation'></a>
```cs
public class CountedAggregate: IRevisioned
{
    // This will be the aggregate version
    public int Version { get; set; }

    public Guid Id
    {
        get;
        set;
    }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }
}

public class ExplicitCounter: SingleStreamProjection<CountedAggregate, Guid>
{
    public override CountedAggregate Evolve(CountedAggregate snapshot, Guid id, IEvent e)
    {
        snapshot ??= new CountedAggregate();

        switch (e.Data)
        {
            case AEvent:
                snapshot.ACount++;
                break;
            case BEvent:
                snapshot.BCount++;
                break;
            case CEvent:
                snapshot.CCount++;
                break;
            case DEvent:
                snapshot.DCount++;
                break;
        }

        // You have to explicitly return the new value
        // of the aggregated document no matter what!
        return snapshot;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/using_explicit_code_for_live_aggregation.cs#L98-L146' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_simple_explicit_code_for_live_aggregation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that this usage is valid for all possible projection lifecycles now (`Live`, `Inline`, and `Async`).
