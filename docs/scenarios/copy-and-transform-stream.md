# Copy and transform stream

This scenario demonstrates how to copy and transform event stream to enable

* Introduction of new events
* Deletion of events

## Scenario

Lets say we have an event stream, from which we would like to delete events of specific kind. Furthermore, we have a new event type that we would like to compose from existing data (akin to versioning). In the sample below, we setup our initial stream.

<!-- snippet: sample_scenario-copyandtransformstream-setup -->
<a id='snippet-sample_scenario-copyandtransformstream-setup'></a>
```cs
var started = new QuestStarted { Name = "Find the Orb" };
var joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
var slayed1 = new MonsterSlayed { Name = "Troll" };
var slayed2 = new MonsterSlayed { Name = "Dragon" };

using (var session = theStore.OpenSession())
{
    session.Events.StartStream<Quest>(started.Name,started, joined, slayed1, slayed2);
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/ScenarioCopyAndReplaceStream.cs#L33-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-copyandtransformstream-setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Next, we introduce a new event type to expand the `MembersJoined` to a series of events, one for each member.

<!-- snippet: sample_scenario-copyandtransformstream-newevent -->
<a id='snippet-sample_scenario-copyandtransformstream-newevent'></a>
```cs
public class MemberJoined
{
    public int Day { get; set; }
    public string Location { get; set; }
    public string Name { get; set; }

    public MemberJoined()
    {
    }

    public MemberJoined(int day, string location, string name)
    {
        Day = day;
        Location = location;
        Name = name;
    }

    public static MemberJoined[] From(MembersJoined @event)
    {
        return @event.Members.Select(x => new MemberJoined(@event.Day, @event.Location, x)).ToArray();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/ScenarioCopyAndReplaceStream.cs#L110-L133' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-copyandtransformstream-newevent' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, we want trolls (`MonsterSlayed`) removed from our stream. However, the stream is a series of ordered, immutable data, with no functionality to patch or otherwise modify existing data. Instead of trying to mutate the stream, we can use the copy and transform pattern to introduce a new event stream. We do this by copying the existing stream to a new one, while applying any needed transforms to the event data being copied. We also make sure to copy any metadata from the original events, and apply a new header to the events to track which stream they came from.

<!-- snippet: sample_scenario-copyandtransformstream-transform -->
<a id='snippet-sample_scenario-copyandtransformstream-transform'></a>
```cs
using (var session = theStore.OpenSession())
{
    var events = session.Events.FetchStream(started.Name);

    var transformedEvents = events.SelectMany(x =>
    {
        // Reapply existing metadata
        session.Events.CopyMetadata(x, x.Data);

        switch (x.Data)
        {
            case MonsterSlayed monster:
            {
                // Trolls we remove from our transformed stream
                if (monster.Name.Equals("Troll")) return Array.Empty<object>();

                session.Events.ApplyHeader("copied_from_event", x.Id, monster);
                return new[] { monster };
            }
            case MembersJoined members:
            {
                // MembersJoined events we transform into a series of events
                var membersEvents = MemberJoined.From(members).Cast<object>().ToArray();
                session.Events.ApplyHeader("copied_from_event", x.Id, events: membersEvents);
                return membersEvents;
            }
        }

        session.Events.ApplyHeader("copied_from_event", x.Id, x.Data);
        return new[] { x.Data };
    }).Where(x => x != null).ToArray();

    // Add "moved from" header to all events being written to new stream 
    session.Events.ApplyHeader("moved_from_stream", started.Name, transformedEvents);

    var moveTo = $"{started.Name} without Trolls";
    // Mark the old stream as moved.
    // This is done first in order for inline projections to handle the StreamMovedTo event before the moved events
    // Furthermore, we assert on the new expected stream version to guard against any racing updates
    session.Events.Append(started.Name, events.Count + 1, new StreamMovedTo
    {
        To = moveTo
    });

    // We copy the transformed events to a new stream
    session.Events.StartStream<Quest>(moveTo, transformedEvents);

    // Transactionally update the streams.
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/ScenarioCopyAndReplaceStream.cs#L46-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-copyandtransformstream-transform' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As the new stream is produced, within the same transaction we introduce an event dictating the stream being copied to have been moved. This should serve as an indication to no longer append new events into the stream. Furthermore, it ensures that the underlying stream being copied has not changed during the copy & transform process (as we assert on the expected stream version).

<!-- snippet: sample_scenario-copyandtransformstream-streammoved -->
<a id='snippet-sample_scenario-copyandtransformstream-streammoved'></a>
```cs
public class StreamMovedTo
{
    public string To { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/ScenarioCopyAndReplaceStream.cs#L135-L140' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-copyandtransformstream-streammoved' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
