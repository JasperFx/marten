# Projections

::: tip
The CQRS style of architecture does not require Event Sourcing, and Event Sourcing can technically be used without CQRS,
but the two concepts go together very well and our documentation pretty well assumes you're going to use Marten within
a CQRS architecture.
:::

So you've made the decision to use all the power of Event Sourcing to capture all the state changes in your system as first class events
and that's now your system of record. Now you have all of these little blobs of JSON floating around your database that represent
state changes, but wouldn't it be nice to actually know what the current state of the system really is? And have that information
in regular old .NET objects that are easy to consume in your code, or maybe even in plain old flat database tables for easy
reporting?

That's where Marten's projection subsystem comes into play:

![Marten Projections](/images/projections.png "Marten Projections")

To explain projections, let's think about the *why*, *how*, and *when* of projections. 

As implied by the diagram above, the **role** of a projection in your system fits into one of the buckets below:

A "write model" is used by command handlers in your system to support decision making. Because this information absolutely needs
to be strongly consistent with the current state of the captured events, you might well strip down the projected model to only
the information your command handlers need.

A "read model" is information that is supplied to clients of your system like user interfaces or other systems. While there's
actually no real mechanical difference between a "read model" and a "write model" (and it's perfectly fine to use the same .NET types
for both roles in some cases), the "read model" is often more rich in the information it relays. For a common example, a "write model"
may omit the names of people involved in an activity and only refer to raw identifiers while a "read model" for a user interface
will include the related names and contact information for the people related to the system state.

By "query model," I really just mean a read-only view of the current event state that is persisted in the database in such a way
(denormalized?) where it is mechanically simple for your system to use LINQ or SQL queries against the system state for reporting
or dashboard type screens.

::: tip
Arguably the single biggest advantage of Marten as an Event Sourcing solution over many other Event Stores is how seamless the integration is between
Marten's "PostgreSQL as Document Database" features and the Event Sourcing storage.
:::

As for "how" projections in Marten work, at a high level the built in projections are taking the raw event data and doing one of:

1. Query the raw event data into memory, and use those events to build up an in memory .NET object that aggregates the state of those events. This is what we'll refer to as [Live Aggregation](/events/projections/live-aggregates)
   in the rest of the documentation
2. Aggregate or translate the raw event data into .NET objects that are persisted as [Marten documents](/documents/) where they can be queried, loaded, or even deleted
   with all of the normal Marten document database features. 
3. Aggregate or translate the raw event data into flat tables in the underlying PostgreSQL database because hey, PostgreSQL is an outstanding relational database and there are plenty of use cases where that's probably your best approach. This is what we refer to as [Flat Table Projections](/events/projections/flat).

Next, let's talk about *when* projections are calculated by discussing Marten's concept of `ProjectionLifecycle`. 

Marten varies a little bit in that projections can be executed with three different lifecycles as shown in the code below:

<!-- snippet: sample_registering_projections_with_different_lifecycles -->
<a id='snippet-sample_registering_projections_with_different_lifecycles'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // Just in case you need Marten to "know" about a projection that will
    // only be calculated "Live", you can register it upfront
    opts.Projections.Add<MySpecialProjection>(ProjectionLifecycle.Live);

    // Or instead, we want strong consistency at all times
    // so that the stored projection documents always exactly reflect
    // the
    opts.Projections.Add<MySpecialProjection>(ProjectionLifecycle.Inline);

    // Or even differently, we can live with eventual consistency and
    // let Marten use its "Async Daemon" to continuously update the stored
    // documents being built out by our projection in the background
    opts.Projections.Add<MySpecialProjection>(ProjectionLifecycle.Async);

    // Just for the sake of completeness, "self-aggregating" types
    // can be registered as projections in Marten with this syntax
    // where "Snapshot" now means "a version of the projection from the events"
    opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
    opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Async);

    // This is the equivalent of ProjectionLifecycle.Live
    opts.Projections.LiveStreamAggregation<QuestParty>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/RegisteringProjections.cs#L14-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_projections_with_different_lifecycles' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For more information, see:

1. [Inline Projections](/events/projections/inline) (`ProjectionLifecycle.Inline`) are executed at the time of event capture and in the same unit of work to persist the projected documents
1. [Live Aggregations](/events/projections/live-aggregates) (`ProjectionLifecycle.Live`) are executed on demand by loading event data and creating the projected view in memory without persisting the projected documents
1. [Asynchronous Projections](/events/projections/async-daemon) (`ProjectionLifecycle.Async`) are executed by a background process (eventual consistency)

For other descriptions of the *Projections* pattern inside of Event Sourcing architectures, see:

- [Projections in Event Sourcing](https://zimarev.com/blog/event-sourcing/projections/)
- [Projections in Event Sourcing: Build ANY model you want!](https://codeopinion.com/projections-in-event-sourcing-build-any-model-you-want/)

Now, let's move on to *how* you build projections in Marten with a discussion of...

## Choosing a Projection Type

:::tip
Do note that all the various types of aggregated projections inherit from a common base type and have the same core set of conventions. The aggregation conventions are best explained in the [Aggregate Projections](/events/projections/aggregate-projections) page.
:::

Marten supplies X main recipes for constructing projections.

1. [Single Stream Projections](/events/projections/aggregate-projections) combine events from a single stream into a single view.
2. [Multi Stream Projections](/events/projections/multi-stream-projections) are a specialized form of projection that allows you to aggregate a view against arbitrary groupings of events across streams.
3. [Event Projections](/events/projections/event-projections) are a recipe for building projections that create or delete one or more documents for a single event
4. [Custom Aggregations](/events/projections/custom-aggregates) are a recipe for building aggregate projections that require more logic than
   can be accomplished by the other aggregation types. Example usages are soft-deleted aggregate documents that maybe be recreated later or
   if you only apply events to an aggregate if the aggregate document previously existed.
5. If one of the built in projection recipes doesn't fit what you want to do, you can happily build your own [custom projection](/events/projections/custom)

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/DocSamples/EventSourcingQuickstart.cs#L149-L161' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_events-aggregate-on-the-fly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

There is also a matching asynchronous `AggregateStreamAsync()` mechanism as well. Additionally, you can do stream aggregations in batch queries with
`IBatchQuery.Events.AggregateStream<T>(streamId)`.

## Inline Projections

*First off, be aware that some event metadata (`IEvent.Version` and `IEvent.Sequence`) is not available during the execution of inline projections when using the ["Quick" append mode](/events/appending). If you need to use this metadata in your projections, please use asynchronous or live projections, or use the "Rich" append mode.*

If you would prefer that the projected aggregate document be updated *inline* with the events being appended, you simply need to register the aggregation type in the `StoreOptions` upfront when you build up your document store like this:

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

## Logging in Projections <Badge type="tip" text="8.19" />

*If* you are running Marten within a .NET application that bootstraps an `IHost` and registering Marten through
`AddMarten()`, all the projections registered in your Marten application will have an instance property for the
`ILogger` like this:

<!-- snippet: sample_using_Logger_in_projections -->
<a id='snippet-sample_using_logger_in_projections'></a>
```cs
// If you have to be all special and want to group the logging
// your own way, just override this method:
public override void AttachLogger(ILoggerFactory loggerFactory)
{
    Logger = loggerFactory.CreateLogger<MyLoggingMarkerType>();
}

public void Project(
    IEvent<AppointmentStarted> @event,
    IDocumentOperations ops)
{
    // Outside of AddMarten() usage, this would be a NullLogger
    // Inside of an app bootstrapped as an IHost with standard .NET
    // logging registered and Marten bootstrapped through AddMarten(),
    // Logger would be an ILogger<T> *by default* where T is the concrete
    // type of the actual projection
    Logger?.LogDebug("Hey, I'm inserting a row for appointment started");

    var sql = "insert into appointment_duration "
              + "(id, start) values (?, ?)";
    ops.QueueSqlCommand(sql,
        @event.Id,
        @event.Timestamp);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/TeleHealth/AppointmentDurationProjection.cs#L29-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_logger_in_projections' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
If you are registering a projection into your DI container, you are responsible for injecting and configuring 
the correct `ILogger`.
:::

## Rebuilding Projections

Projections need to be rebuilt when the code that defines them changes in a way that requires events to be reapplied in order to maintain correct state. Using an `IDaemon` this is easy to execute on-demand:

Refer to [Rebuilding Projections](/events/projections/rebuilding) for more details.

::: warning
Marten by default while creating new object tries to use <b>default constructor</b>. Default constructor doesn't have to be public, might be also private or protected.

If class does not have the default constructor then it creates an uninitialized object (see [the Microsoft documentation](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.formatterservices.getuninitializedobject?view=netframework-4.8) for more info)

Because of that, no member initializers will be run so all of them need to be initialized in the event handler methods.
:::

## Projection Lifecycles

See the opening section and the discussion of `ProjectionLifecycle`.
