<!--Title:Projections-->
<!--Url:projections-->

<div class="alert alert-info">
The Marten community is working to create more samples of event store projections. Check this page again soon. In the meantime, don't forget to just look through the code and our unit tests.
</div>

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

<[sample:MonsterDefeatedTransform]>

Now, we can plug our new transform type above as a projection when we configure our document store like this:

<[sample:applying-monster-defeated]>

<[sample:using_live_transformed_events]>

## Aggregates

Aggregates condense data described by a single stream. As of v1.0, Marten only supports aggregation via .Net classes. Aggregates are calculated upon every request by running the event stream through them, as compared to inline projections, which are computed at event commit time and stored as documents.

The out-of-the box convention is to expose `public Apply([Event Type])` methods on your aggregate class to do all incremental updates to an aggregate object. This can be customised using [AggregatorLookup](#aggregator-lookup).

Sticking with the fantasy theme, the `QuestParty` class shown below could be used to aggregate streams of quest data:

<[sample:QuestParty]>

New in Marten 1.2 is the ability to use `Event<T>` metadata within your projections, assuming that you're not trying to run the aggregations inline.

The syntax using the built in aggregation technique is to take in `Event<T>` as the argument to your `Apply(event)` methods,
where `T` is the event type you're interested in:

<[sample:QuestPartyWithEvents]>

### Aggregates Across Multiple Streams

Example coming soon, and check [Jeremy's blog](http://jeremydmiller.com) for a sample soon.

It's possible currently by using either a custom `IProjection` or using the existing aggregation capabilities with a
custom `IAggregateFinder<T>`, where `T` is the projected view document type.

### Aggregator Lookup

`EventGraph.UseAggregatorLookup(IAggregatorLookup aggregatorLookup)` can be used to register an `IAggregatorLookup` that is used to look up `IAggregator<T>` for aggregations. This allows a generic aggregation strategy to be used, rather than registering aggregators case-by-case through `EventGraphAddAggregator<T>(IAggregator<T> aggregator)`.

A shorthand extension method `EventGraph.UseAggregatorLookup(this EventGraph eventGraph, AggregationLookupStrategy strategy)` can be used to set default aggregation lookup, whereby

- `AggregationLookupStrategy.UsePublicApply` resolves aggregators that use public Apply
- `AggregationLookupStrategy.UsePrivateApply` resolves aggregators that use private Apply
- `AggregationLookupStrategy.UsePublicAndPrivateApply` resolves aggregators that use public or private Apply

The aggregation lookup can also be set in the `StoreOptions.Events.UserAggregatorLookup`

<[sample:register-custom-aggregator-lookup]>

### Live Aggregation via .Net

You can always fetch a stream of events and build an aggregate completely live from the current event data by using this syntax:

<[sample:events-aggregate-on-the-fly]>

There is also a matching asynchronous `AggregateStreamAsync()` mechanism as well. Additionally, you can do stream aggregations in batch queries with
`IBatchQuery.Events.AggregateStream<T>(streamId)`.

## Inline Projections

_First off, be aware that event metadata (e.g. stream version and sequence number) are not available duing the execution of inline projections. If you need to use event metadata in your projections, please use asynchronous or live projections._

If you would prefer that the projected aggregate document be updated _inline_ with the events being appended, you simply need to register the aggregation type in the `StoreOptions` upfront when you build up your document store like this:

<[sample:registering-quest-party]>

At this point, you would be able to query against `QuestParty` as just another document type.

## Rebuilding Projections

Projections need to be rebuilt when the code that defines them changes in a way that requires events to be reapplied in order to maintain correct state. Using an `IDaemon` this is easy to execute on-demand:

<[sample:rebuild-single-projection]>
