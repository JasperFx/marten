# Marten as Event Store

Marten's Event Store functionality is a powerful way to utilize Postgresql in the [event sourcing](http://martinfowler.com/eaaDev/EventSourcing.html) style of persistence in your application. Beyond simple event capture and access to the raw event
stream data, Marten also helps you create "read side" views of the raw event data through its support for projections.

## Event Store quick start

There is not anything special you need to do to enable the event store functionality in Marten, and it obeys the same rules about automatic schema generation described in [schema](guide/schema/]>. Marten is just a client library,
and there's nothing to install other than the Marten NuGet.

Because I’ve read way too much epic fantasy fiction, my sample problem domain is an application that records, analyses, and visualizes the status of quests. During a quest, you may want to record events like:

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/QuestTypes.cs#L12-L144' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-events' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_sample-events-1'></a>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/QuestTypes.cs#L12-L144' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-events-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, let's say that we're starting a new "quest" with the first couple of events, then appending a couple more as other quest party members join up:

<!-- snippet: sample_event-store-start-stream-with-explicit-type -->
<a id='snippet-sample_event-store-start-stream-with-explicit-type'></a>
```cs
using (var session = store.OpenSession())
{
    var started = new QuestStarted { Name = "Destroy the One Ring" };
    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

    // Start a brand new stream and commit the new events as
    // part of a transaction
    session.Events.StartStream(typeof(Quest), questId, started, joined1);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/event_store_quickstart.cs#L45-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event-store-start-stream-with-explicit-type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In addition to generic `StartStream<T>`, `IEventStore` has a non-generic `StartStream` overload that let you pass explicit type.

<!-- snippet: sample_event-store-quickstart -->
<a id='snippet-sample_event-store-quickstart'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.Projections.SelfAggregate<QuestParty>();
});

var questId = Guid.NewGuid();

using (var session = store.OpenSession())
{
    var started = new QuestStarted { Name = "Destroy the One Ring" };
    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

    // Start a brand new stream and commit the new events as
    // part of a transaction
    session.Events.StartStream<Quest>(questId, started, joined1);
    session.SaveChanges();

    // Append more events to the same stream
    var joined2 = new MembersJoined(3, "Buckland", "Merry", "Pippen");
    var joined3 = new MembersJoined(10, "Bree", "Aragorn");
    var arrived = new ArrivedAtLocation { Day = 15, Location = "Rivendell" };
    session.Events.Append(questId, joined2, joined3, arrived);
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/event_store_quickstart.cs#L16-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event-store-quickstart' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It has also overload to create streams without associating them with aggregate type (stored in `mt_streams` table).

<!-- snippet: sample_event-store-start-stream-with-explicit-type -->
<a id='snippet-sample_event-store-start-stream-with-explicit-type'></a>
```cs
using (var session = store.OpenSession())
{
    var started = new QuestStarted { Name = "Destroy the One Ring" };
    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Sam");

    // Start a brand new stream and commit the new events as
    // part of a transaction
    session.Events.StartStream(typeof(Quest), questId, started, joined1);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/event_store_quickstart.cs#L45-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event-store-start-stream-with-explicit-type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
