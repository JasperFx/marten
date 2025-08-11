# Event Store Quick Start

There's nothing special you need to do to enable the event store functionality in Marten, it obeys the same rules of automatic schema generation as described in [schema](/schema/). Given you've followed the [Getting Started](/getting-started) guide, you're all ready to go.

Because I’ve read way too much epic fantasy fiction, my sample problem domain is an application that records, analyses, and visualizes the status of heroic quests (destroying the One Ring, recovering Aldur's Orb, recovering the Horn of Valere, etc.). During a quest, you may want to record events like:

<!-- snippet: sample_sample-events -->
<a id='snippet-sample_sample-events'></a>
```cs
public sealed record ArrivedAtLocation(Guid QuestId, int Day, string Location);

public sealed record MembersJoined(Guid QuestId, int Day, string Location, string[] Members);

public sealed record QuestStarted(Guid QuestId, string Name);

public sealed record QuestEnded(Guid QuestId, string Name);

public sealed record MembersDeparted(Guid QuestId, int Day, string Location, string[] Members);

public sealed record MembersEscaped(Guid QuestId, string Location, string[] Members);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L9-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_event-store-quickstart -->
<a id='snippet-sample_event-store-quickstart'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
});

var questId = Guid.NewGuid();

await using var session = store.LightweightSession();
var started = new QuestStarted(questId, "Destroy the One Ring");
var joined1 = new MembersJoined(questId,1, "Hobbiton", ["Frodo", "Sam"]);

// Start a brand new stream and commit the new events as
// part of a transaction
session.Events.StartStream(questId, started, joined1);

// Append more events to the same stream
var joined2 = new MembersJoined(questId,3, "Buckland", ["Merry", "Pippen"]);
var joined3 = new MembersJoined(questId,10, "Bree", ["Aragorn"]);
var arrived = new ArrivedAtLocation(questId, 15, "Rivendell");
session.Events.Append(questId, joined2, joined3, arrived);

// Save the pending changes to db
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L91-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_event-store-quickstart' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At some point we would like to know what members are currently part of the quest party. To keep things simple, we're going to use Marten's _live_ stream aggregation feature to model a `QuestParty` that updates itself based on our events:

<!-- snippet: sample_QuestParty -->
<a id='snippet-sample_QuestParty'></a>
```cs
public sealed record QuestParty(Guid Id, List<string> Members)
{
    // These methods take in events and update the QuestParty
    public static QuestParty Create(QuestStarted started) => new(started.QuestId, []);
    public static QuestParty Apply(MembersJoined joined, QuestParty party) =>
        party with
        {
            Members = party.Members.Union(joined.Members).ToList()
        };

    public static QuestParty Apply(MembersDeparted departed, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !departed.Members.Contains(x)).ToList()
        };

    public static QuestParty Apply(MembersEscaped escaped, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !escaped.Members.Contains(x)).ToList()
        };
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L27-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_QuestParty' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Next, we'll use the live projection to aggregate the quest stream for a single quest party like this:

<!-- snippet: sample_events-aggregate-on-the-fly -->
<a id='snippet-sample_events-aggregate-on-the-fly'></a>
```cs
await using var session2 = store.LightweightSession();
// questId is the id of the stream
var party = await session2.Events.AggregateStreamAsync<QuestParty>(questId);

var party_at_version_3 = await session2.Events
    .AggregateStreamAsync<QuestParty>(questId, 3);

var party_yesterday = await session2.Events
    .AggregateStreamAsync<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L119-L131' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_events-aggregate-on-the-fly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Simple, right? The above code will load the events from the database and run them through the `Create` & `Apply` handlers of the `QuestParty` projection, returning the current state of our party.

What about the quest itself? On top of seeing our in-progress quest, we also want the ability to query our entire history of past quests. For this, we'll create an _inline_ `SingleStreamProjection` that persists our Quest state to the database as the events are being written:

<!-- snippet: sample_Quest -->
<a id='snippet-sample_Quest'></a>
```cs
public sealed record Quest(Guid Id, List<string> Members, List<string> Slayed, string Name, bool isFinished);

public sealed class QuestProjection: SingleStreamProjection<Quest, Guid>
{
    public static Quest Create(QuestStarted started) => new(started.QuestId, [], [], started.Name, false);
    public static Quest Apply(MembersJoined joined, Quest party) =>
        party with
        {
            Members = party.Members.Union(joined.Members).ToList()
        };

    public static Quest Apply(MembersDeparted departed, Quest party) =>
        party with
        {
            Members = party.Members.Where(x => !departed.Members.Contains(x)).ToList()
        };

    public static Quest Apply(MembersEscaped escaped, Quest party) =>
        party with
        {
            Members = party.Members.Where(x => !escaped.Members.Contains(x)).ToList()
        };

    public static Quest Apply(QuestEnded ended, Quest party) =>
        party with { isFinished = true };

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L54-L83' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_Quest' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
Marten is highly flexible in the way you wish to describe projections (classes, records, static/non-static handlers, single/multi stream projections etc), see [Projections](/events/projections/) for more information. 
:::

Our projection should be registered to the document store like so:

<!-- snippet: sample_adding-quest-projection -->
<a id='snippet-sample_adding-quest-projection'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.Projections.Add<QuestProjection>(ProjectionLifecycle.Inline); // [!code ++]
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L137-L143' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_adding-quest-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then we can persist some events and immediately query the state of our quest:

<!-- snippet: sample_querying-quest-projection -->
<a id='snippet-sample_querying-quest-projection'></a>
```cs
await using var session = store.LightweightSession();

var started = new QuestStarted(questId, "Destroy the One Ring");
var joined1 = new MembersJoined(questId, 1, "Hobbiton", ["Frodo", "Sam"]);

session.Events.StartStream(questId, started, joined1);
await session.SaveChangesAsync();

// we can now query the quest state like any other Marten document
var questState = await session.LoadAsync<Quest>(questId);

var finishedQuests = await session.Query<Quest>().Where(x => x.isFinished).ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L147-L161' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying-quest-projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
