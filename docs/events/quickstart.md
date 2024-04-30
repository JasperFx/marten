# Event Store Quick Start

There is not anything special you need to do to enable the event store functionality in Marten, and it obeys the same rules about automatic schema generation described in [schema](/schema/). Marten is just a client library, and there's nothing to install other than the Marten NuGet.

Because I’ve read way too much epic fantasy fiction, my sample problem domain is an application that records, analyses, and visualizes the status of heroic quests (destroying the One Ring, recovering Aldur's Orb, recovering the Horn of Valere, etc.). During a quest, you may want to record events like:

<!-- snippet: sample_sample-events -->
<a id='snippet-sample_sample-events'></a>
```cs
public class ArrivedAtLocation
{
    public int Day { get; set; }

    public string Location { get; set; }

    public override string ToString()
    {
        return $"Arrived at {Location} on Day {Day}";
    }
}

public class MembersJoined
{
    public MembersJoined()
    {
    }

    public MembersJoined(int day, string location, params string[] members)
    {
        Day = day;
        Location = location;
        Members = members;
    }

    public Guid QuestId { get; set; }

    public int Day { get; set; }

    public string Location { get; set; }

    public string[] Members { get; set; }

    public override string ToString()
    {
        return $"Members {Members.Join(", ")} joined at {Location} on Day {Day}";
    }

    protected bool Equals(MembersJoined other)
    {
        return QuestId.Equals(other.QuestId) && Day == other.Day && Location == other.Location && Members.SequenceEqual(other.Members);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((MembersJoined) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(QuestId, Day, Location, Members);
    }
}

public class QuestStarted
{
    public string Name { get; set; }
    public Guid Id { get; set; }

    public override string ToString()
    {
        return $"Quest {Name} started";
    }

    protected bool Equals(QuestStarted other)
    {
        return Name == other.Name && Id.Equals(other.Id);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((QuestStarted) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Id);
    }
}

public class QuestEnded
{
    public string Name { get; set; }
    public Guid Id { get; set; }

    public override string ToString()
    {
        return $"Quest {Name} ended";
    }
}

public class MembersDeparted
{
    public Guid Id { get; set; }

    public Guid QuestId { get; set; }

    public int Day { get; set; }

    public string Location { get; set; }

    public string[] Members { get; set; }

    public override string ToString()
    {
        return $"Members {Members.Join(", ")} departed at {Location} on Day {Day}";
    }
}

public class MembersEscaped
{
    public Guid Id { get; set; }

    public Guid QuestId { get; set; }

    public string Location { get; set; }

    public string[] Members { get; set; }

    public override string ToString()
    {
        return $"Members {Members.Join(", ")} escaped from {Location}";
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/QuestTypes.cs#L12-L144' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_event-store-quickstart -->
<a id='snippet-sample_event-store-quickstart'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
});

var questId = Guid.NewGuid();

await using (var session = store.LightweightSession())
{
    var started = new QuestStarted { Name = "Destroy the One Ring" };
    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

    // Start a brand new stream and commit the new events as
    // part of a transaction
    session.Events.StartStream<Quest>(questId, started, joined1);

    // Append more events to the same stream
    var joined2 = new MembersJoined(3, "Buckland", "Merry", "Pippen");
    var joined3 = new MembersJoined(10, "Bree", "Aragorn");
    var arrived = new ArrivedAtLocation { Day = 15, Location = "Rivendell" };
    session.Events.Append(questId, joined2, joined3, arrived);

    // Save the pending changes to db
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/event_store_quickstart.cs#L17-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event-store-quickstart' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In addition to generic `StartStream<T>`, `IEventStore` has a non-generic `StartStream` overload that let you pass explicit type.

<!-- snippet: sample_event-store-start-stream-with-explicit-type -->
<a id='snippet-sample_event-store-start-stream-with-explicit-type'></a>
```cs
await using (var session = store.LightweightSession())
{
    var started = new QuestStarted { Name = "Destroy the One Ring" };
    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

    // Start a brand new stream and commit the new events as
    // part of a transaction
    session.Events.StartStream(typeof(Quest), questId, started, joined1);
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/event_store_quickstart.cs#L49-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event-store-start-stream-with-explicit-type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, we would at some point like to see the current state of the quest party to check up on where they're at, who is in the party, and maybe how many monsters they've slain along the way. To keep things simple, we're going to use Marten's live stream aggregation feature to model a `QuestParty` that can update itself based on our events:

<!-- snippet: sample_QuestParty -->
<a id='snippet-sample_questparty'></a>
```cs
public class QuestParty
{
    public List<string> Members { get; set; } = new();
    public IList<string> Slayed { get; } = new List<string>();
    public string Key { get; set; }
    public string Name { get; set; }

    // In this particular case, this is also the stream id for the quest events
    public Guid Id { get; set; }

    // These methods take in events and update the QuestParty
    public void Apply(MembersJoined joined) => Members.Fill(joined.Members);
    public void Apply(MembersDeparted departed) => Members.RemoveAll(x => departed.Members.Contains(x));
    public void Apply(QuestStarted started) => Name = started.Name;

    public override string ToString()
    {
        return $"Quest party '{Name}' is {Members.Join(", ")}";
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/QuestParty.cs#L8-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_questparty' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And next, we'll use a live projection to build an aggregate for a single quest party like this:

<!-- snippet: sample_events-aggregate-on-the-fly -->
<a id='snippet-sample_events-aggregate-on-the-fly'></a>
```cs
await using (var session = store.LightweightSession())
{
    // questId is the id of the stream
    var party = session.Events.AggregateStream<QuestParty>(questId);
    Console.WriteLine(party);

    var party_at_version_3 = await session.Events
        .AggregateStreamAsync<QuestParty>(questId, 3);

    var party_yesterday = await session.Events
        .AggregateStreamAsync<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/event_store_quickstart.cs#L93-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_events-aggregate-on-the-fly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->