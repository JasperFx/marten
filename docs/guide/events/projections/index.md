# Projections

::: tip INFO
The Marten community is working to create more samples of event store projections. Check this page again soon. In the meantime, don't forget to just look through the code and our unit tests.
:::

First, some terminology that we're going to use throughout this section:

* _Projection_ - any strategy for generating "read side" views from the raw event streams
* _Transformation_ - a type of projection that generates or updates a single read side view for a single event
* _Aggregate_ - a type of projection that "aggregates" data from multiple events to create a single readside view document
* _Inline Projections_ - a type of projection that executes as part of any event capture transaction and is stored as a document
* _Async Projections_ - a type of projection that runs in a background process using an [eventual consistency](https://en.wikipedia.org/wiki/Eventual_consistency) strategy, and is stored as a document
* _Live Projections_ - evaluates a projected view from the raw event data on demand within Marten

## Transformations

Transformations project from one event type to one document. If you want to have certain events projected to a readside document and the relationship is one to one, Marten supports this pattern today with the .Net `ITransform` interface:

<[sample:ITransform]>

As a sample problem, let's say that we're constantly capturing `MonsterSlayed` events and our system needs to query just this data. You could query directly against the large `mt_events` table with
`IEventStore.Query<MonsterSlayed>()`, but it would be more efficient to keep a separate "read side" copy of this data in a new data collection. We could build a new transform class and readside document like this:

<!-- snippet: sample_MonsterDefeatedTransform -->
<a id='snippet-sample_monsterdefeatedtransform'></a>
```cs
public class MonsterDefeatedTransform: EventProjection
{
    public MonsterDefeated Transform(IEvent<MonsterSlayed> input)
    {
        return new MonsterDefeated
        {
            Id = input.Id,
            Monster = input.Data.Name
        };
    }
}

public class MonsterDefeated
{
    public Guid Id { get; set; }
    public string Monster { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/inline_transformation_of_events.cs#L122-L141' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_monsterdefeatedtransform' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, we can plug our new transform type above as a projection when we configure our document store like this:

<!-- snippet: sample_applying-monster-defeated -->
<a id='snippet-sample_applying-monster-defeated'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "monster_defeated";

    _.Projections.Add(new MonsterDefeatedTransform());
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/inline_transformation_of_events.cs#L82-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_applying-monster-defeated' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_using_live_transformed_events -->
<a id='snippet-sample_using_live_transformed_events'></a>
```cs
public void using_live_transformed_events(IDocumentSession session)
{
    var started = new QuestStarted { Name = "Find the Orb" };
    var joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" } };
    var slayed1 = new MonsterSlayed { Name = "Troll" };
    var slayed2 = new MonsterSlayed { Name = "Dragon" };

    MembersJoined joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

    session.Events.StartStream<Quest>(started, joined, slayed1, slayed2);
    session.SaveChanges();

    // Our MonsterDefeated documents are created inline
    // with the SaveChanges() call above and are available
    // for querying
    session.Query<MonsterDefeated>().Count()
        .ShouldBe(2);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/event_store_quickstart.cs#L153-L173' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_live_transformed_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Aggregates

Aggregates condense data described by a single stream. As of v1.0, Marten only supports aggregation via .Net classes. Aggregates are calculated upon every request by running the event stream through them, as compared to inline projections, which are computed at event commit time and stored as documents.

The out-of-the box convention is to expose `public Apply([Event Type])` methods on your aggregate class to do all incremental updates to an aggregate object. This can be customised using [AggregatorLookup](#aggregator-lookup).

Sticking with the fantasy theme, the `QuestParty` class shown below could be used to aggregate streams of quest data:

<!-- snippet: sample_QuestParty -->
<a id='snippet-sample_questparty'></a>
```cs
public class QuestParty
{
    public List<string> Members { get; set; } = new();

    public IList<string> Slayed { get; } = new List<string>();

    public void Apply(MembersJoined joined)
    {
        Members.Fill(joined.Members);
    }

    public void Apply(MembersDeparted departed)
    {
        Members.RemoveAll(x => departed.Members.Contains(x));
    }

    public void Apply(QuestStarted started)
    {
        Name = started.Name;
    }

    public string Key { get; set; }

    public string Name { get; set; }

    public Guid Id { get; set; }

    public override string ToString()
    {
        return $"Quest party '{Name}' is {Members.Join(", ")}";
    }
}

public class QuestFinishingParty: QuestParty
{
    private readonly string _exMachina;

    public QuestFinishingParty()
    {
    }

    public QuestFinishingParty(string exMachina)
    {
        _exMachina = exMachina;
    }

    public void Apply(MembersEscaped escaped)
    {
        if (_exMachina == null)
        {
            throw new NullReferenceException("Can't escape w/o an Ex Machina");
        }

        Members.RemoveAll(x => escaped.Members.Contains(x));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/QuestParty.cs#L8-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_questparty' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

New in Marten 1.2 is the ability to use `Event<T>` metadata within your projections, assuming that you're not trying to run the aggregations inline.

The syntax using the built in aggregation technique is to take in `Event<T>` as the argument to your `Apply(event)` methods,
where `T` is the event type you're interested in:

<!-- snippet: sample_QuestPartyWithEvents -->
<a id='snippet-sample_questpartywithevents'></a>
```cs
public class QuestPartyWithEvents
{
    private readonly IList<string> _members = new List<string>();

    public string[] Members
    {
        get
        {
            return _members.ToArray();
        }
        set
        {
            _members.Clear();
            _members.AddRange(value);
        }
    }

    public IList<string> Slayed { get; } = new List<string>();

    public void Apply(MembersJoined joined)
    {
        _members.Fill(joined.Members);
    }

    public void Apply(MembersDeparted departed)
    {
        _members.RemoveAll(x => departed.Members.Contains(x));
    }

    public void Apply(QuestStarted started)
    {
        Name = started.Name;
    }

    public string Name { get; set; }

    public Guid Id { get; set; }

    public override string ToString()
    {
        return $"Quest party '{Name}' is {Members.Join(", ")}";
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/QuestPartyWithEvents.cs#L9-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_questpartywithevents' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Aggregates Across Multiple Streams

Example coming soon, and check [Jeremy's blog](http://jeremydmiller.com) for a sample soon.

It's possible currently by using either a custom `IProjection` or using the existing aggregation capabilities with a
custom `IAggregateFinder<T>`, where `T` is the projected view document type.

### Aggregator Lookup

`EventGraph.UseAggregatorLookup(IAggregatorLookup aggregatorLookup)` can be used to register an `IAggregatorLookup` that is used to look up `IAggregator<T>` for aggregations. This allows a generic aggregation strategy to be used, rather than registering aggregators case-by-case through `EventGraphAddAggregator<T>(IAggregator<T> aggregator)`.

A shorthand extension method `EventGraph.UseAggregatorLookup(this EventGraph eventGraph, AggregationLookupStrategy strategy)` can be used to set default aggregation lookup, whereby

* `AggregationLookupStrategy.UsePublicApply` resolves aggregators that use public Apply
* `AggregationLookupStrategy.UsePrivateApply` resolves aggregators that use private Apply
* `AggregationLookupStrategy.UsePublicAndPrivateApply` resolves aggregators that use public or private Apply

The aggregation lookup can also be set in the `StoreOptions.Events.UserAggregatorLookup`

// TODO: fix this sample
<[sample:register-custom-aggregator-lookup]>

### Live Aggregation via .Net

You can always fetch a stream of events and build an aggregate completely live from the current event data by using this syntax:

<!-- snippet: sample_events-aggregate-on-the-fly -->
<a id='snippet-sample_events-aggregate-on-the-fly'></a>
```cs
using (var session = store.OpenSession())
{
    // questId is the id of the stream
    var party = session.Events.AggregateStream<QuestParty>(questId);
    Console.WriteLine(party);

    var party_at_version_3 = session.Events
        .AggregateStream<QuestParty>(questId, 3);

    var party_yesterday = session.Events
        .AggregateStream<QuestParty>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/event_store_quickstart.cs#L81-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_events-aggregate-on-the-fly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

There is also a matching asynchronous `AggregateStreamAsync()` mechanism as well. Additionally, you can do stream aggregations in batch queries with
`IBatchQuery.Events.AggregateStream<T>(streamId)`.

## Inline Projections

_First off, be aware that event metadata (e.g. stream version and sequence number) are not available duing the execution of inline projections. If you need to use event metadata in your projections, please use asynchronous or live projections._

If you would prefer that the projected aggregate document be updated _inline_ with the events being appended, you simply need to register the aggregation type in the `StoreOptions` upfront when you build up your document store like this:

<!-- snippet: sample_registering-quest-party -->
<a id='snippet-sample_registering-quest-party'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.Events.TenancyStyle = tenancyStyle;
    _.DatabaseSchemaName = "quest_sample";
    if (tenancyStyle == TenancyStyle.Conjoined)
    {
        _.Schema.For<QuestParty>().MultiTenanted();
    }

    // This is all you need to create the QuestParty projected
    // view
    _.Projections.SelfAggregate<QuestParty>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Projections/inline_aggregation_by_stream_with_multiples.cs#L23-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering-quest-party' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At this point, you would be able to query against `QuestParty` as just another document type.

## Rebuilding Projections

Projections need to be rebuilt when the code that defines them changes in a way that requires events to be reapplied in order to maintain correct state. Using an `IDaemon` this is easy to execute on-demand:

// TODO: fix this sample
<[sample:rebuild-single-projection]>

::: warning
Marten by default while creating new object tries to use <b>default constructor</b>. Default constructor doesn't have to be public, might be also private or protected.

If class does not have the default constructor then it creates an uninitialized object (see [here](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.formatterservices.getuninitializedobject?view=netframework-4.8) for more info)

Because of that, no member initializers will be run so all of them need to be initialized in the event handler methods.
:::
