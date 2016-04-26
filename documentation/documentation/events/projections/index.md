<!--Title:Projections-->
<!--Url:projections-->

Projections are very new in Marten, but we plan on much more support and new use cases in the near future.

## The Vision

A couple years ago, I got to do what turned into a proof of concept project for building out an event store on top of Postgresql’s JSON support. My thought for Marten’s projection support is 
largely taken from [this blog post I wrote on the earlier attempt at writing an event store on Postgresql](https://jeremydmiller.com/2014/10/22/building-an-eventstore-with-user-defined-projections-on-top-of-postgresql-and-node-js/).

Today the projection ability is very limited. So far you can use the live or “inline” aggregation of a single stream shown above or a simple pattern that allows you to create a single readside document for a given event type.

The end state we envision is to be able to allow users to:

* Express projections in either .Net code or by using Javascript functions running inside of Postgresql itself
* To execute the projection building either “inline” with event capture for pure ACID, asynchronously for complicated aggregations or better performance (and there comes eventual consistency back into our lives), or do aggregations “live” on demand. We think that this break down of projection     timings will give users the ability to handle systems with many writes, but few reads with on demand projections, or to handle systems with few writes, but many reads with inline projections.
* To provide and out of the box “async daemon” that you would host as a stateful process within your applications to continuously calculate projections in the background. We want to at least experiment with using Postgresql’s NOTIFY/LISTEN functionality to avoid making this a purely polling process.
* Support hooks to perform your own form of event stream processing using the existing IDocumentSessionListener mechanism and maybe some way to plug more processors into the queue reading in the async daemon described above
* Add some “snapshotting” functionality that allows you to perform aggregated views on top of occasional snapshots every X times an event is captured on an aggregate
* Aggregate data across streams
* Support arbitrary categorization of events across streams



## Projecting from One Event to One Document    

If you want to have certain events projected to a readside document and the relationship is one to one, Marten supports this pattern today with the .Net `ITransform` interface:

<[sample:ITransform]>

As a sample problem, let's say that we're constantly capturing `MonsterSlayed` events and our system needs to query just this data. You could query directly against the big ol' `mt_events` table with 
`IEventStore.Query<MonsterSlayed>()`, but it would be more efficient to keep a separate "read side" copy of this data in a new data collection. We could build a new transform class and readside document like this:

<[sample:MonsterDefeatedTransform]>

Now, we can plug our new transform type above as a projection when we configure our document store like this:

<[sample:applying-monster-defeated]>

<[sample:using_live_transformed_events]>

## Aggregated Views Across a Single Stream

As of now (v0.9), Marten is only supporting aggregation via .Net classes. The out of the box convention is to expose `Apply([Event Type])` methods
on your aggregate class to do all incremental updates to an aggregate object. Sticking with the fantasy theme, the `QuestParty` class shown below
could be used to aggregate streams of quest data:

<[sample:QuestParty]>


## Live Aggregation via .Net

You can always fetch a stream of events and build an aggregate completely live from the current event data by using this syntax:

<[sample:events-aggregate-on-the-fly]>

There is also a matching asynchronous `AggregateStreamAsync()` mechanism as well. Additionally, you can do stream aggregations in batch queries with
`IBatchQuery.Events.AggregateStream<T>(streamId)`.




## Inline Aggregation

If you would prefer that the projected aggregate document be updated _inline_ with the events being appended, you simply need to register
the aggregation type in the `StoreOptions` upfront when you build up your document store like this:

<[sample:registering-quest-party]>

At this point, you would be able to query against `QuestParty` as just another document type.