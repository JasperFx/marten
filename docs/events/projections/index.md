# Projections

Marten has a strong model for user-defined projections of the raw event data. Projections are used within Marten to create read-side views of the raw event data.

## Choosing a Projection Type

:::tip
Do note that all the various types of aggregated projections inherit from a common base type and have the same core set of conventions. The aggregation conventions are best explained in the [Aggregate Projections](/events/projections/aggregate-projections) page.
:::

1. [Single Stream Projections](/events/projections/aggregate-projections) combine events from a single stream into a single view.
2. [Multi Stream Projections](/events/projections/multi-stream-projections) are a specialized form of projection that allows you to aggregate a view against arbitrary groupings of events across streams.
3. [Event Projections](/events/projections/event-projections) are a recipe for building projections that create or delete one or more documents for a single event
4. [Custom Aggregations](/events/projections/custom-aggregates) are a recipe for building aggregate projections that require more logic than
   can be accomplished by the other aggregation types. Example usages are soft-deleted aggregate documents that maybe be recreated later or
   if you only apply events to an aggregate if the aggregate document previously existed.
5. If one of the built in projection recipes doesn't fit what you want to do, you can happily build your own [custom projection](/events/projections/custom)

## Projection Lifecycles

Marten varies a little bit in that projections can be executed with three different lifecycles:

1. [Inline Projections](/events/projections/inline) are executed at the time of event capture and in the same unit of work to persist the projected documents
1. [Live Aggregations](/events/projections/live-aggregates) are executed on demand by loading event data and creating the projected view in memory without persisting the projected documents
1. [Asynchronous Projections](/events/projections/async-daemon) are executed by a background process (eventual consistency)

For other descriptions of the _Projections_ pattern inside of Event Sourcing architectures, see:

- [Projections in Event Sourcing](https://zimarev.com/blog/event-sourcing/projections/)
- [Projections in Event Sourcing: Build ANY model you want!](https://codeopinion.com/projections-in-event-sourcing-build-any-model-you-want/)

## Aggregates

Aggregates condense data described by a single stream. Marten only supports aggregation via .Net classes. Aggregates are calculated upon every request by running the event stream through them, as compared to inline projections, which are computed at event commit time and stored as documents.

The out-of-the box convention is to expose `public Apply(<EventType>)` methods on your aggregate class to do all incremental updates to an aggregate object.

Sticking with the fantasy theme, the `QuestParty` class shown below could be used to aggregate streams of quest data:

<!-- snippet: sample_QuestParty -->
<a id='snippet-sample_questparty'></a>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L27-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_questparty' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Live Aggregation via .Net

You can always fetch a stream of events and build an aggregate completely live from the current event data by using this syntax:

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

There is also a matching asynchronous `AggregateStreamAsync()` mechanism as well. Additionally, you can do stream aggregations in batch queries with
`IBatchQuery.Events.AggregateStream<T>(streamId)`.

## Inline Projections

_First off, be aware that some event metadata (`IEvent.Version` and `IEvent.Sequence`) is not available during the execution of inline projections when using the ["Quick" append mode](/events/appending). If you need to use this metadata in your projections, please use asynchronous or live projections, or use the "Rich" append mode._

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
    _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/inline_aggregation_by_stream_with_multiples.cs#L32-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering-quest-party' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At this point, you would be able to query against `QuestParty` as just another document type.

## Rebuilding Projections

Projections need to be rebuilt when the code that defines them changes in a way that requires events to be reapplied in order to maintain correct state. Using an `IDaemon` this is easy to execute on-demand:

Refer to [Rebuilding Projections](/events/projections/rebuilding) for more details.

::: warning
Marten by default while creating new object tries to use <b>default constructor</b>. Default constructor doesn't have to be public, might be also private or protected.

If class does not have the default constructor then it creates an uninitialized object (see [the Microsoft documentation](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.formatterservices.getuninitializedobject?view=netframework-4.8) for more info)

Because of that, no member initializers will be run so all of them need to be initialized in the event handler methods.
:::
