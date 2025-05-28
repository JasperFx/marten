# Stream Compacting <Badge type="tip" text="8.0" />

One of the earliest lessons I learned designing software systems is that reigning in unchecked growth of databases through judicious pruning and archiving can do wonders for system performance over time. As yet another tool in the toolbox for scaling Marten and in collaboration with a JasperFx Software customer, we’re adding an important feature in Marten 8.0 called “Stream Compacting” that can be used to judiciously shrink Marten’s event storage to keep the database a little more limber as old data is no longer relevant.

Let’s say that you failed to be omniscient in your event stream modeling and ended up with a longer stream of events than you’d ideally like and that is bloating your database size and maybe impacting performance. Maybe you’re going to be in a spot where you don’t really care about all the old events, but really just want to maintain the current projected state and more recent events. And maybe you’d like to throw the old events in some kind of “cold” storage like an S3 bucket or [something to be determined later].

Enter the new “Stream Compacting.” Let's say that you have event streams for a piece of equipment in a job site, and record
events every time the equipment is moved around the job site (this is based on an IoT system that one of our users built out with Marten),
and the stream for a piece of `Equipment` can grow quite large over time -- but you don't necessarily care about events older
than a couple months. This is a great opportunity to employ stream compacting:

<!-- snippet: sample_using_stream_compacting -->
<a id='snippet-sample_using_stream_compacting'></a>
```cs
public static async Task compact(IDocumentSession session, Guid equipmentId, IEventsArchiver archiver)
{
    // Maybe we have ceased to care about old movements of a piece of equipment
    // But we want to retain an accurate positioning over the past year
    // Yes, maybe we should have done a "closing the books" pattern, but we didn't
    // So instead, let's just "compact" the stream

    await session.Events.CompactStreamAsync<Equipment>(equipmentId, x =>
    {
        // We could say "compact" all events for this stream
        // from version 1000 and below
        x.Version = 1000;

        // Or instead say, "compact all events older than 30 days ago":
        x.Timestamp = DateTimeOffset.UtcNow.Subtract(30.Days());

        // Carry out some kind of user defined archiving process to
        // "move" the about to be archived events to something like an S3 bucket
        // or an Azure Blob or even just to another table
        x.Archiver = archiver;

        // Pass in a cancellation token because this might take a bit...
        x.CancellationToken = CancellationToken.None;
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/stream_compacting.cs#L32-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_stream_compacting' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

What this “compacting” does is effectively create a snapshot of the stream state and
replaces the existing events that are archived in the database with a single `Compacted<Equipment>` event with this shape at the version position
that it replaced:

```cs
// Right now we're just "compacting" in place, but there's some
// thought to extending this to what one of our contributors
// calls "re-streaming" in their system where they write out an
// all new stream that just starts with a summary
public record Compacted<T>(T Snapshot, Guid PreviousStreamId, string PreviousStreamKey)
```

The latest, greatest Marten projection bits are always able to restart any single stream projection with the `Snapshot` data of a `Compacted<T>` event, with no additional coding on your part.

There's not yet any default archiver, but we're open to suggestions about what that might be in the future. To carry out event archival, supply
an implementation of this interface:

<!-- snippet: sample_IEventsArchiver -->
<a id='snippet-sample_ieventsarchiver'></a>
```cs
/// <summary>
/// Callback interface for executing event archiving
/// </summary>
public interface IEventsArchiver
{
    Task MaybeArchiveAsync<T>(IDocumentOperations operations, StreamCompactingRequest<T> request, IReadOnlyList<IEvent> events,
        CancellationToken cancellation);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/EventStore.StreamCompacting.cs#L166-L177' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ieventsarchiver' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By default, Marten is *not* archiving events in this operation.

Stream compacting will not have any adverse impact on running asynchronous projections and even carrying out a compacting operation while an 
asynchronous projection happens to be working on exactly that stream will not cause any discrepancies with the daemon as it runs.

You can compact a single stream repeatedly over time. For example, you may choose to compact a stream any time every time it becomes over 
a 1,000 events long. In that case, Marten is completely replacing the `Compacted<T>` with the new snapshot version. The old `Compacted<T>` event
is not itself archived.

You can still rewind and replay a single stream projection even if it has been compacted, but only to the point where the compacting 
took place. Marten may be able to "recover" archived events in a future release.

Stream compacting will not play well if there are more than one single stream projection views for the same type of stream. 
This isn't an insurmountable problem, but it's definitely not convenient. I think you’d have to explicitly handle a `Compacted<T1>`
event in the projection for T2 if both T1 and T2 are separate views of the same stream type. 

Lastly, stream compacting is a complement to the [event stream archiving](/events/archiving) functionality, and not a replacement. 
You may want to use both together.
