# Using Event Metadata

Marten automatically collects [metadata about the events](/events/metadata) you capture as well as allowing
you to customize the metadata at will. All of that information (versions, timestamps, headers)
is available to be used within aggregation projections.

## Aggregate Versioning

It's frequently valuable to know the version of the underlying event stream that a single stream aggregate represents. Marten 5.4 added a
new, built in convention to automatically set the aggregate version on the aggregate document itself. The immediate usage is probably to help
Marten users opt into Marten's [optimistic concurrency for appending events](/events/appending.html#appending-events-1) by making it easier to get the current aggregate (stream) version that you need
in order to opt into the optimistic concurrency check.

To start with, let's say we have an `OrderAggregate` defined like this:

<!-- snippet: sample_OrderAggregate_with_version -->
<a id='snippet-sample_orderaggregate_with_version'></a>
```cs
public class OrderAggregate
{
    // This is most likely the stream id
    public Guid Id { get; set; }

    // This would be set automatically by Marten if
    // used as the target of a SingleStreamAggregation
    public int Version { get; set; }

    public void Apply(OrderShipped shipped) => HasShipped = true;
    public bool HasShipped { get; private set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/OrderAggregate.cs#L6-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_orderaggregate_with_version' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Notice the `Version` property of that document above. Using a naming convention (we'll talk about how to go around the convention in just a second),
Marten "knows" that that property should reflect the latest versioned event within the individual stream encountered by this projection. So if
there have been 5 events captured for a particular stream and all five events have been processed through the projection, the value of the `Version`
property will be 5.

There are of course some restrictions:

- The version member can be either a field or a property
- The getter can be internal or private (but the mechanics are a tiny bit smoother with a public setter)
- The version member can be either an `int` (Int32) or `long` (Int64)

Marten determines whether a member is the version of the aggregate by first finding all public members
of either type `int` or `long`, then running down these rules:

1. A member marked with the `[Version]` attribute will override the naming convention
2. Look for an member named "version" (it's not case sensitive)
3. **But**, ignore any member marked with `[MartenIgnore]` in case "Version" has a different meaning on your aggregate document

## Using Event Metadata in Aggregates

All the previous examples showed `Apply` / `Create` / `ShouldDelete` methods that accepted
the specific event type as the first argument. If there is a need for accessing the
event metadata (timestamps, causation/correlation information, custom event headers),
you can alternatively accept an argument of type `IEvent<T>` where `T` is the actual event type (do this in
place of the event body) or by accepting an additional argument of type `IEvent`
just to access the event metadata.

Below is a small example of accessing event metadata during aggregation:

<!-- snippet: sample_aggregation_using_event_metadata -->
<a id='snippet-sample_aggregation_using_event_metadata'></a>
```cs
public class TripProjection: SingleStreamProjection<Trip, Guid>
{
    // Access event metadata through IEvent<T>
    public Trip Create(IEvent<TripStarted> @event)
    {
        var trip = new Trip
        {
            Id = @event.StreamId, // Marten does this for you anyway
            Started = @event.Timestamp,
            CorrelationId = @event.Timestamp, // Open telemetry type tracing
            Description = @event.Data.Description // Still access to the event body
        };

        // Use a custom header
        if (@event.Headers.TryGetValue("customer", out var customerId))
        {
            trip.CustomerId = (string)customerId;
        }

        return trip;
    }

    public void Apply(TripEnded ended, Trip trip, IEvent @event)
    {
        trip.Ended = @event.Timestamp;
    }

    // Other Apply/ShouldDelete methods

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<Trip> slice)
    {
        // Emit other events or messages during asynchronous projection
        // processing

        // Access to the current state as of the projection
        // event page being processed *right* now
        var currentTrip = slice.Snapshot;

        if (currentTrip.TotalMiles > 1000)
        {
            // Append a new event to this stream
            slice.AppendEvent(new PassedThousandMiles());

            // Append a new event to a different event stream by
            // first specifying a different stream id
            slice.AppendEvent(currentTrip.InsuranceCompanyId, new IncrementThousandMileTrips());

            // "Publish" outgoing messages when the event page is successfully committed
            slice.PublishMessage(new SendCongratulationsOnLongTrip(currentTrip.Id));

            // And yep, you can make additional changes to Marten
            operations.Store(new CompletelyDifferentDocument
            {
                Name = "New Trip Segment",
                OriginalTripId = currentTrip.Id
            });
        }

        // This usage has to be async in case you're
        // doing any additional data access with the
        // Marten operations
        return new ValueTask();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/TripProjectionWithEventMetadata.cs#L31-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregation_using_event_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Working with Event Metadata <Badge type="tip" text="7.12" />

::: info
As of Marten 7.33, this mechanism executes for every single event in the current event slice in order.
:::

At any point in an `Apply()` or `Create()` or `ShouldDelete()` method, you can take in either the generic `IEvent` wrapper
or the specific `IEvent<T>` wrapper type for the specific event. _Sometimes_ though, you may want to automatically tag your
aggregated document with metadata from applied events. _If_ you are using
either `SingleStreamProjection<T>` or `MultiStreamProjection<TDoc, TId>` as the base class for a projection, you can
override the `ApplyMetadata(T aggregate, IEvent lastEvent)` method in your projection to manually map event metadata to
your aggregate in any way you wish.

Here's an example of using a custom header value of the events captured to update an aggregate based on the last event encountered:

<!-- snippet: sample_using_ApplyMetadata -->
<a id='snippet-sample_using_applymetadata'></a>
```cs
public class Item
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public bool Started { get; set; }
    public DateTimeOffset WorkedOn { get; set; }
    public bool Completed { get; set; }
    public string LastModifiedBy { get; set; }
    public DateTimeOffset? LastModified { get; set; }

    public int Version { get; set; }
}

public record ItemStarted(string Description);

public record ItemWorked;

public record ItemFinished;

public class ItemProjection: SingleStreamProjection<Item, Guid>
{
    public void Apply(Item item, ItemStarted started)
    {
        item.Started = true;
        item.Description = started.Description;
    }

    public void Apply(Item item, IEvent<ItemWorked> worked)
    {
        // Nothing, I know, this is weird
    }

    public void Apply(Item item, ItemFinished finished)
    {
        item.Completed = true;
    }

    public override Item ApplyMetadata(Item aggregate, IEvent lastEvent)
    {
        // Apply the last timestamp
        aggregate.LastModified = lastEvent.Timestamp;

        var person = lastEvent.GetHeader("last-modified-by");

        aggregate.LastModifiedBy = person?.ToString() ?? "System";

        return aggregate;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/using_apply_metadata.cs#L173-L225' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_applymetadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And the same projection in usage in a unit test to see how it's all put together:

<!-- snippet: sample_apply_metadata -->
<a id='snippet-sample_apply_metadata'></a>
```cs
[Fact]
public async Task apply_metadata()
{
    StoreOptions(opts =>
    {
        opts.Projections.Add<ItemProjection>(ProjectionLifecycle.Inline);

        // THIS IS NECESSARY FOR THIS SAMPLE!
        opts.Events.MetadataConfig.HeadersEnabled = true;
    });

    // Setting a header value on the session, which will get tagged on each
    // event captured by the current session
    theSession.SetHeader("last-modified-by", "Glenn Frey");

    var id = theSession.Events.StartStream<Item>(new ItemStarted("Blue item")).Id;
    await theSession.SaveChangesAsync();

    theSession.Events.Append(id, new ItemWorked(), new ItemWorked(), new ItemFinished());
    await theSession.SaveChangesAsync();

    var item = await theSession.LoadAsync<Item>(id);

    // RIP Glenn Frey, take it easy!
    item.LastModifiedBy.ShouldBe("Glenn Frey");
    item.Version.ShouldBe(4);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/using_apply_metadata.cs#L18-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_apply_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
