<!--Title:Projections-->
<!--Url:projections-->

<div class="alert alert-info">
The Marten community is working to create more samples of event store projections. Check this page again soon. In the meantime,
don't forget to just look through the code and our unit tests.
</div>

First, some terminology that we're going to use throughout this section:

* _Projection_ - any strategy for generating "read side" views from the raw event streams
* _Transformation_ - a type of projection that generates or updates a single read side view for a single event
* _Aggregate_ - a type of projection that "aggregates" data from multiple events to create a single readside view document
* _Inline Projections_ - projection's that are executed as part of any event capture transaction
* _Async Projections_ - projection's that run in some kind of background process using an [eventual consistency](https://en.wikipedia.org/wiki/Eventual_consistency) strategy
* _Live Projections_ - evaluating a projected view from the raw event data on demand within Marten


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

As of now (v1.0), Marten is only supporting aggregation via .Net classes. The out of the box convention is to expose `Apply([Event Type])` methods
on your aggregate class to do all incremental updates to an aggregate object. Sticking with the fantasy theme, the `QuestParty` class shown below
could be used to aggregate streams of quest data:

<[sample:QuestParty]>

## Aggregated Views Across Multiple Streams

Example coming soon, and check [Jeremy's blog](http://jeremydmiller.com) for a sample soon.

It's possible today by using either a custom `IProjection` or using the existing aggregation capabilities with a
custom `IAggregateFinder<T>`, where "T" is the projected view document type.




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


