# Archiving Event Streams

Like most (all?) event stores, Marten is designed around the idea of the events being persisted to a single file, immutable
log of events. All the same though, there are going to be problem domains where certain event streams become obsolete. Maybe
because a workflow is completed, maybe through time based expiry rules, or maybe because a customer or user is removed
from the system. To help optimize Marten's event store usage, you can take advantage of the stream archiving to 
mark events as archived on a stream by stream basis. 

::: warning
You can obviously use pure SQL to modify the events persisted by Marten. While that might be valuable in some cases,
we urge you to be cautious about doing so.
:::

The impact of archiving an event stream is:

* In the "classic" usage of Marten, the relevant stream and event rows are marked with an `is_archived = TRUE`
* With the "opt in" table partitioning model for "hot/cold" storage described in the next section, the stream and event rows are
  moved to the archived partition tables for streams and events
* The [async daemon](/events/projections/async-daemon) subsystem process that processes projections and subscriptions in a background process automatically ignores
  archived events -- but that can be modified on a per projection/subscription basis
* Archived events are excluded by default from any event data queries through the LINQ support in Marten

To mark a stream as archived, it's just this syntax:

<!-- snippet: sample_archive_stream_usage -->
<a id='snippet-sample_archive_stream_usage'></a>
```cs
public async Task SampleArchive(IDocumentSession session, string streamId)
{
    session.Events.ArchiveStream(streamId);
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L34-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_archive_stream_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As in all cases with an `IDocumentSession`, you need to call `SaveChanges()` to commit the
unit of work.

::: tip
At this point, you will also have to manually delete any projected aggregates based on the event streams being
archived if that is desirable
:::

The `mt_events` and `mt_streams` tables both have a boolean column named `is_archived`.

Archived events are filtered out of all event Linq queries by default. But of course, there's a way
to query for archived events with the `IsArchived` property of `IEvent` as shown below:

<!-- snippet: sample_querying_for_archived_events -->
<a id='snippet-sample_querying_for_archived_events'></a>
```cs
var events = await theSession.Events
    .QueryAllRawEvents()
    .Where(x => x.IsArchived)
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L234-L241' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_for_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also query for all events both archived and not archived with `MaybeArchived()`
like so:

<!-- snippet: sample_query_for_maybe_archived_events -->
<a id='snippet-sample_query_for_maybe_archived_events'></a>
```cs
var events = await theSession.Events.QueryAllRawEvents()
    .Where(x => x.MaybeArchived()).ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/archiving_events.cs#L269-L274' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_for_maybe_archived_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Hot/Cold Storage Partitioning <Badge type="tip" text="7.25" />

::: warning
This option will only be beneficial if you are being aggressive about marking obsolete, old, or expired event data
as archived.
:::

Want your system using Marten to scale and perform even better than it already does? If you're leveraging
event archiving in your application workflow, you can possibly derive some significant performance and scalability
improvements by opting into using PostgreSQL native table partitioning on the event and event stream data
to partition the "hot" (active) and "cold" (archived) events into separate partition tables. 

The long and short of this option is that it keeps the active `mt_streams` and `mt_events` tables smaller, which pretty
well always results in better performance over time.

The simple flag for this option is:

<!-- snippet: sample_turn_on_stream_archival_partitioning -->
<a id='snippet-sample_turn_on_stream_archival_partitioning'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // Turn on the PostgreSQL table partitioning for
    // hot/cold storage on archived events
    opts.Events.UseArchivedStreamPartitioning = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/Optimizations.cs#L13-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_turn_on_stream_archival_partitioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
If you are turning this option on to an existing system, you may want to run the database schema migration script
by hand rather than trying to let Marten do it automatically. The data migration from non-partitioned to partitioned
will probably require system downtime because it actually has to copy the old table data, drop the old table, create the new 
table, copy all the existing data from the temp table to the new partitioned table, and finally drop the temporary table.
:::

## Archived Event <Badge type="tip" text="7.34" />

Marten has a built in event named `Archived` that can be appended to any event stream:

<!-- snippet: sample_Archived_event -->
<a id='snippet-sample_archived_event'></a>
```cs
namespace Marten.Events;

/// <summary>
/// The presence of this event marks a stream as "archived" when it is processed
/// by a single stream projection of any sort
/// </summary>
public record Archived(string Reason);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Archived.cs#L1-L11' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_archived_event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When this event is appended to an event stream *and* that event is processed through any type of single stream projection
for that event stream (snapshot or what we used to call a "self-aggregate", `SingleStreamProjection`, or `CustomProjection` with the `AggregateByStream` option),
Marten will automatically mark that entire event stream as archived as part of processing the projection. This applies for
both `Inline` and `Async` execution of projections. 

Let's try to make this concrete by building a simple order processing system that might include this
aggregate:

<!-- snippet: sample_Order_for_optimized_command_handling -->
<a id='snippet-sample_order_for_optimized_command_handling'></a>
```cs
public class Item
{
    public string Name { get; set; }
    public bool Ready { get; set; }
}

public class Order
{
    // This would be the stream id
    public Guid Id { get; set; }

    // This is important, by Marten convention this would
    // be the
    public int Version { get; set; }

    public Order(OrderCreated created)
    {
        foreach (var item in created.Items)
        {
            Items[item.Name] = item;
        }
    }

    public void Apply(IEvent<OrderShipped> shipped) => Shipped = shipped.Timestamp;
    public void Apply(ItemReady ready) => Items[ready.Name].Ready = true;

    public DateTimeOffset? Shipped { get; private set; }

    public Dictionary<string, Item> Items { get; set; } = new();

    public bool IsReadyToShip()
    {
        return Shipped == null && Items.Values.All(x => x.Ready);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L23-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_order_for_optimized_command_handling' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Next, let's say we're having the `Order` aggregate snapshotted so that it's updated every time new events 
are captured like so:

<!-- snippet: sample_registering_Order_as_Inline -->
<a id='snippet-sample_registering_order_as_inline'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // The Order aggregate is updated Inline inside the
    // same transaction as the events being appended
    opts.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);

    // Opt into an optimization for the inline aggregates
    // used with FetchForWriting()
    opts.Projections.UseIdentityMapForAggregates = true;
})

// This is also a performance optimization in Marten to disable the
// identity map tracking overall in Marten sessions if you don't
// need that tracking at runtime
.UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L209-L230' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_order_as_inline' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, let's say as a way to keep our application performing as well as possible, we'd like to be aggressive about archiving
shipped orders to keep the "hot" event storage table small. One way we can do that is to append the `Archived` event 
as part of processing a command to ship an order like so:

<!-- snippet: sample_handling_shiporder_and_emitting_archived_event -->
<a id='snippet-sample_handling_shiporder_and_emitting_archived_event'></a>
```cs
public static async Task HandleAsync(ShipOrder command, IDocumentSession session)
{
    var stream = await session.Events.FetchForWriting<Order>(command.OrderId);
    var order = stream.Aggregate;

    if (!order.Shipped.HasValue)
    {
        // Mark it as shipped
        stream.AppendOne(new OrderShipped());

        // But also, the order is done, so let's mark it as archived too!
        stream.AppendOne(new Archived("Shipped"));

        await session.SaveChangesAsync();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/OptimizedCommandHandling.cs#L233-L252' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_handling_shiporder_and_emitting_archived_event' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If an `Order` hasn't already shipped, one of the outcomes of that command handler executing is that the entire event stream
for the `Order` will be marked as archived. 

::: info
This was originally conceived as a way to improve the Wolverine aggregate handler workflow usability while also encouraging
Marten users to take advantage of the event archiving feature. 
:::
