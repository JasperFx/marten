# Event Store Improvements for V7

This document is brainstorming for improvements to the Event Store functionality in Marten V7. This does not include features meant for
"CritterStackPro" products, but does include some necessary baseline infrastructure for planned "CritterStackPro" features.

In some cases, this document outlines "poor man's" subset strategies for features that we will build in a more robust (and inevitably more complicated) way 
in "CritterStackPro"

The goal is to start a discussion about what is and isn't going to be in scope for V7. When this document is "complete", the output is a new set
of GitHub issues marked for the V7 release. 


## Raise events from asynchronous projection updates

I'm going to allow Oskar address this one:-)

From my side, I think the `ISessionListener` approach we have today is weak and should be a little more "outbox'd" such that it only fires off when
the transaction is complete. 

For the Wolverine integration, I'd like to be able to enroll in Wolverine's outbox as part of processing a page of events in the daemon to send out messages using the 
aggregation data as part of the outgoing message.

More generically, we need a good way to emit new events that will be processed as appends within the same daemon transaction. This might be a little tricky and will
require a spike.

**Gotta do this in a way such that users can opt into whether the new events should be emitted during projection rebuilds**


## Sharding the Event Store tables

This is a [long standing issue](https://github.com/JasperFx/marten/issues/770) that we didn't get around to in the huge V4 release.

Some notes:

* My personal proposal is to use native PostgreSQL sharding on the `is_archived` column to effect a "hot storage, cold storage" approach. This should be paired with
  advice to users to be more aggressive about archiving event streams that are obsolete/stale/not active as a way to improve throughput over all. The daemon and every
  other place where we query events automatically uses `is_archived = false` filters anyway
* We probably shouldn't be sharding on anything that would impact the async daemon's "high water detection" that uses the event sequence. In other words, we
  want to make it as easy as possible to walk the sequence number values within a single table
* **Maybe** it would be valuable to shard the active events and event streams on the event sequence number ranges. That would be beneficial to the async daemon, if
  harmful to live aggregations
* This is going to be an ugly migration, which is why is was cut from V4. If you apply this after the fact, you'll have to have some down time. You'll have to copy the existing mt_streams & mt_events tables off to the side, then drop both tables, create the new table partitions and the virtual table that points to the partitions, then copy in all the events back into the virtual table so postgres can sort the actual records around


## Sharding the Document Tables

Same migration issues as the event tables, but there's just more possibilities here. We'll need a significant investment in Weasel for the migrations. There's
some nascent partition support in Weasel already. Need some easy ways for users to create their own partitioning strategies


## IEventSession interface/service

What if we had a new interface specifically for folks who really only use the event sourcing at different areas of the code that got you straight to 
event sourcing behavior like:

```csharp
public interface IEventSession : IEventStore
{
    Task SaveChangesAsync(CancellationToken token = default);
}
```

It'd be the `DocumentSession`, but with a more event store friendly interface so you don't have to do `session.Events.******` on everything. 


## Document by document type identity map behavior

Today, document sessions either have the identity map behavior for all document types or no identity map for all types. I think especially around the 
projections, that it would be helpful to selectively turn on the identity map within the session for certain document types. I think this is actually pretty
low hanging fruit because all you'd do is switch up the cached `IDocumentStorage` objects for a certain document type.


## Ensure that aggregates updated "Inline" are coming from identity map

Imagine this very common scenario:

A CQRS command handler needs to:

1. Fetch an aggregate of an event stream as a "write model" to process the command
2. Emit some events
3. Maybe update the projected aggregation if it's running `Inline`
4. For whatever reason, access the current state of the aggregate again in the handler. Maybe because it's a response type in an HTTP handler, maybe to transmit
   messages, who knows

Maybe you even want the updated aggregate to make some kind of sense of the next part of the work -- which I'd at least push Wolverine folks to do with cascading messages, but still

Regardless of the Marten `IDocumentSession` type, I think it would be valuable to **prevent** unnecessary extra reads of the aggregate, especially if there's some
re-application of the events. 

At a minimum, I think that the aggregation fetching should use the identity map *even if the session itself isn't using the identity map*. See the next item.
This probably impacts both the `FetchForWriting()` behavior and the inline projection behavior

This absolutely impacts the Wolverine "aggregate handler workflow". There's an opportunity to eliminate some serious inefficiency today


## Versioned Projections for Blue/Green deployment

This is really just sticking a "projection version number" on the Projection subclasses that would potentially be used as a suffix on the document
tables for the projected documents (the document alias). This would enable blue/green deployment of the same projection so that nodes on the previous version can be 
using the previous revision/definition of the projection while other nodes are running the newer model. 

Obviously there's some serious concern about just how the new projection gets rebuilt in time. I think the "catch up" mode that this document
introduces as an "Async strategy for FetchForWriting()" is necessary to make this viable


## "Poor Man's Load Balancing" of the Async Daemon

See Oskar's post [How to scale out Marten](https://event-driven.io/en/scaling_out_marten/). I'm voting to just bake this work directly into Marten as a 
first class thing. Do some randomness of the hot/cold behavior for individual projection groups to see if other nodes can take up that work.

I think the async daemon for each tenant database in the case of multi-tenancy through separate databases should be allowed to run on separate nodes. Might take some 
work with randomness to kind of distribute the load per tenant

By no means will this be a true equivalent of the projection load balancing work planned for "CritterStackPro"

## Dynamic Database per Tenant Multi-Tenancy model

A JasperFx client is sponsoring this work, so it's absolutely in scope. This time there'll need to be a "master" database that has a table
to track tenants and tenant databases. The async daemon should be able to discover new tenant databases at runtime and try to spin up a new daemon
for each tenant as a way to de facto discover and activate new tenants.


## "First Class Subscription" model

A new model for subscriptions through the async daemon as described by Oskar here: [https://event-driven.io/en/integrating_Marten/](https://event-driven.io/en/integrating_Marten/)

I think this deserves a slightly different abstraction like:

```csharp
public interface IProjection
{
    Task SubscribeAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation);
}
```

Behind the scenes it's really just just being driven by the daemon running a single page of events at a time, and recording
the process as it does with projections today.

Now, other issues to consider:

* Is this done as a "hot" subscription that starts wherever the event high water mark is already when a new one is added?
* Do we support "cold" subscriptions that would cause a new subscription to start from the the beginning at event #1?
* How is `ISubscription` handled in rebuilds? Do we have specific replays? How do we help users keep from doing this accidentally when they're rebuilding projections?




## FetchForReading alternative

Same functionality as `FetchForWriting()`, but leave out a little bit of the versioning functionality as this would be 
read-only. Encourage users to avoid using `LoadAsync()` or `AggregateStreamAsync()` by hand so that they can more easily
switch between projection lifecycles. This will be almost absolutely necessary when we go to the "zero downtime" projection
rebuilds

## Async strategy for FetchForWriting()

What I've been calling the "catch up" mode. Essentially [this issue](https://github.com/JasperFx/marten/issues/2846). If a per stream aggregation projection
is running in async mode, calling `FetchForWriting()` would fetch the current version of the projected document and
all the events *after* the saved version of the aggregate document in a single database call and apply them on top
of the saved aggregate

I think this is going to require some additional options for "versioned documents". Instead of the `Guid`-based strategy we just today for optimistic versioning, we'd 
need an `int` based model where the stream version is also embedded into the document storage for the projected document. 


## Pluggable FetchForReading/Writing strategies

Today the underlying strategies for how `FetchForWriting` or `FetchForReading` actually works. This is shamelessly meant as a hook
to inject more robust "CritterStackPro" recipes later. 

## StreamAggregate(HttpContext)

Just bring the `FetchForReading()` behavior to the HTTP "streaming json" logic. **If** an async projection is completely caught up, stream the JSON for the persisted model. Otherwise incrementally aggregate it and serialize the results down to JSON