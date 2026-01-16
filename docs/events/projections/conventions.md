# Aggregation with Conventional Methods

## Aggregate Creation

::: tip
As of Marten 7, if your aggregation projection has both a `Create()` function or constructor for an event type, and
an `Apply()` method for the same event type, Marten will only call one or the other method depending on whether the
aggregate already exists **but never both** for one single event.
:::

Aggregates can initially be created behind the scenes by Marten if there's a no-arg constructor function on the aggregate
document type -- which doesn't have to be public by the way.

You can also use a constructor that takes an event type as shown in this sample of a `Trip` stream aggregation:

<!-- snippet: sample_Trip_stream_aggregation -->
<a id='snippet-sample_trip_stream_aggregation'></a>
```cs
public class Trip
{
    // Probably safest to have an empty, default
    // constructor unless you can guarantee that
    // a certain event type will always be first in
    // the event stream
    public Trip()
    {
    }

    // Create a new aggregate based on the initial
    // event type
    internal Trip(TripStarted started)
    {
        StartedOn = started.Day;
        Active = true;
    }

    public Guid Id { get; set; }
    public int EndedOn { get; set; }

    public double Traveled { get; set; }

    public string State { get; set; }

    public bool Active { get; set; }

    public int StartedOn { get; set; }
    public Guid? RepairShopId { get; set; }

    // The Apply() methods would mutate the aggregate state
    internal void Apply(Arrival e) => State = e.State;
    internal void Apply(Travel e) => Traveled += e.TotalDistance();

    internal void Apply(TripEnded e)
    {
        Active = false;
        EndedOn = e.Day;
    }

    // We think stream aggregation is mostly useful for live aggregations,
    // but hey, if you want to use a aggregation as an asynchronous projection,
    // you can also specify when the aggregate document should be deleted
    internal bool ShouldDelete(TripAborted e) => true;
    internal bool ShouldDelete(Breakdown e) => e.IsCritical;
    internal bool ShouldDelete(VacationOver e) => Traveled > 1000;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TestingSupport/TripProjectionWithCustomName.cs#L123-L173' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_trip_stream_aggregation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or finally, you can use a method named `Create()` on a projection type as shown in this sample:

<!-- snippet: sample_TripProjection_aggregate -->
<a id='snippet-sample_tripprojection_aggregate'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        DeleteEvent<TripAborted>();

        DeleteEvent<Breakdown>(x => x.IsCritical);

        DeleteEvent<VacationOver>((trip, _) => trip.Traveled > 1000);
    }

    // These methods can be either public, internal, or private but there's
    // a small performance gain to making them public
    public void Apply(Arrival e, Trip trip) => trip.State = e.State;

    public void Apply(Travel e, Trip trip)
    {
        Debug.WriteLine($"Trip {trip.Id} Traveled " + e.TotalDistance());
        trip.Traveled += e.TotalDistance();
        Debug.WriteLine("New total distance is " + e.TotalDistance());
    }

    public void Apply(TripEnded e, Trip trip)
    {
        trip.Active = false;
        trip.EndedOn = e.Day;
    }

    public Trip Create(IEvent<TripStarted> started)
    {
        return new Trip { Id = started.StreamId, StartedOn = started.Data.Day, Active = true };
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TestingSupport/TripProjectionWithCustomName.cs#L48-L84' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tripprojection_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Create()` method has to return either the aggregate document type or `Task<T>` where `T` is the aggregate document type. There must be an argument for the specific event type or `IEvent<T>` where `T` is the event type if you need access to event metadata. You can also take in an `IQuerySession` if you need to look up additional data as part of the transformation or `IEvent` in addition to the exact event type just to get at event metadata.

## Applying Changes to the Aggregate Document

::: tip
`Apply()` methods or `ProjectEvent<T>()` method calls can also use interfaces or abstract types that are implemented by specific event types, and
Marten will apply all those event types that can be cast to the interface or abstract type to that method when executing the projection.
:::

To make changes to an existing aggregate, you can either use inline Lambda functions per event type with one of the overloads of `ProjectEvent()`:

<!-- snippet: sample_using_ProjectEvent_in_aggregate_projection -->
<a id='snippet-sample_using_projectevent_in_aggregate_projection'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        ProjectEvent<Arrival>((trip, e) => trip.State = e.State);
        ProjectEvent<Travel>((trip, e) => trip.Traveled += e.TotalDistance());
        ProjectEvent<TripEnded>((trip, e) =>
        {
            trip.Active = false;
            trip.EndedOn = e.Day;
        });

        ProjectEventAsync<Breakdown>(async (session, trip, e) =>
        {
            var repairShop = await session.Query<RepairShop>()
                .Where(x => x.State == trip.State)
                .FirstOrDefaultAsync();

            trip.RepairShopId = repairShop?.Id;
        });
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TestingSupport/TripProjectionWithCustomName.cs#L179-L204' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_projectevent_in_aggregate_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

I'm not personally that wild about using lots of inline Lambdas like the example above, and to that end, Marten now supports the `Apply()` method convention. Here's the same `TripProjection`, but this time using methods to mutate the `Trip` document:

<!-- snippet: sample_TripProjection_aggregate -->
<a id='snippet-sample_tripprojection_aggregate'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        DeleteEvent<TripAborted>();

        DeleteEvent<Breakdown>(x => x.IsCritical);

        DeleteEvent<VacationOver>((trip, _) => trip.Traveled > 1000);
    }

    // These methods can be either public, internal, or private but there's
    // a small performance gain to making them public
    public void Apply(Arrival e, Trip trip) => trip.State = e.State;

    public void Apply(Travel e, Trip trip)
    {
        Debug.WriteLine($"Trip {trip.Id} Traveled " + e.TotalDistance());
        trip.Traveled += e.TotalDistance();
        Debug.WriteLine("New total distance is " + e.TotalDistance());
    }

    public void Apply(TripEnded e, Trip trip)
    {
        trip.Active = false;
        trip.EndedOn = e.Day;
    }

    public Trip Create(IEvent<TripStarted> started)
    {
        return new Trip { Id = started.StreamId, StartedOn = started.Data.Day, Active = true };
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TestingSupport/TripProjectionWithCustomName.cs#L48-L84' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tripprojection_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Apply()` methods can accept any combination of these arguments:

1. The actual event type
1. `IEvent<T>` where the `T` is the actual event type. Use this if you want access to the [event metadata](/events/metadata) like versions or timestamps.
1. `IEvent` access the event metadata. It's perfectly valid to accept both `IEvent` for the metadata and the specific event type just out of convenience.
1. `IQuerySession` if you need to do additional data lookups
1. The aggregate type

The valid return types are:

1. `void` if you are mutating the aggregate document
1. The aggregate type itself, and this allows you to use immutable aggregate types
1. `Task` if you are mutating the aggregate document with the use of external data read through `IQuerySession`
1. `Task<T>` where `T` is the aggregate type. This allows you to use immutable aggregate types while also using external data read through `IQuerySession`

## Deleting the Aggregate Document

In asynchronous or inline projections, receiving a certain event may signal that the projected document is now obsolete and should be deleted from
document storage. If a certain event type always signals a deletion to the aggregated view, you can use this mechanism inside of the constructor function of your
aggregate projection type:

<!-- snippet: sample_deleting_aggregate_by_event_type -->
<a id='snippet-sample_deleting_aggregate_by_event_type'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        // The current Trip aggregate would be deleted if
        // the projection encountered a TripAborted event
        DeleteEvent<TripAborted>();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/AggregationExamples.cs#L16-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deleting_aggregate_by_event_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If the deletion of the aggregate document needs to be done by testing some combination of the current aggregate state, the event,
and maybe even other document state in your Marten database, you can use more overloads of `DeleteEvent()` as shown below:

<!-- snippet: sample_deleting_aggregate_by_event_type_and_func -->
<a id='snippet-sample_deleting_aggregate_by_event_type_and_func'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        // The current Trip aggregate would be deleted if
        // the Breakdown event is "critical"
        DeleteEvent<Breakdown>(x => x.IsCritical);

        // Alternatively, delete the aggregate if the trip
        // is currently in New Mexico and the breakdown is critical
        DeleteEvent<Breakdown>((trip, e) => e.IsCritical && trip.State == "New Mexico");

        DeleteEventAsync<Breakdown>(async (session, trip, e) =>
        {
            var anyRepairShopsInState = await session.Query<RepairShop>()
                .Where(x => x.State == trip.State)
                .AnyAsync();

            // Delete the trip if there are no repair shops in
            // the current state
            return !anyRepairShopsInState;
        });
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/AggregationExamples.cs#L40-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deleting_aggregate_by_event_type_and_func' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Another option is to use a method convention with a method named `ShouldDelete()`, with this equivalent using the `ShouldDelete() : bool` method convention:

<!-- snippet: sample_deleting_aggregate_by_event_type_and_func_with_convention -->
<a id='snippet-sample_deleting_aggregate_by_event_type_and_func_with_convention'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    // The current Trip aggregate would be deleted if
    // the Breakdown event is "critical"
    public bool ShouldDelete(Breakdown breakdown) => breakdown.IsCritical;

    // Alternatively, delete the aggregate if the trip
    // is currently in New Mexico and the breakdown is critical
    public bool ShouldDelete(Trip trip, Breakdown breakdown)
        => breakdown.IsCritical && trip.State == "New Mexico";

    public async Task<bool> ShouldDelete(IQuerySession session, Trip trip, Breakdown breakdown)
    {
        var anyRepairShopsInState = await session.Query<RepairShop>()
            .Where(x => x.State == trip.State)
            .AnyAsync();

        // Delete the trip if there are no repair shops in
        // the current state
        return !anyRepairShopsInState;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/AggregationExamples.cs#L81-L106' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deleting_aggregate_by_event_type_and_func_with_convention' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `ShouldDelete()` method can take any combination of these arguments:

1. The actual event type
1. `IEvent<T>` where the `T` is the actual event type. Use this if you want access to the [event metadata](/events/metadata) like versions or timestamps.
1. `IQuerySession` if you need to do additional data lookups
1. The aggregate type

Additionally, `ShouldDelete()` methods should return either a `Boolean` or `Task<Boolean>` if doing data lookups with `IQuerySession` -- and we'very strongly recommend using strictly asynchronous APIs if running the projection asynchronously or using `SaveChangesAsync()` when executing projections inline.
