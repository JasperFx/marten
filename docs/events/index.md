# Marten as Event Store

## What is Event Sourcing?

Event Sourcing is a design pattern in which results of business operations are stored as a series of events.

It is an alternative way to persist data. In contrast with state-oriented persistence that only keeps the latest version of the entity state, Event Sourcing stores each state change as a separate event.

Thanks for that, no business data is lost. Each operation results in the event stored in the database. That enables extended auditing and diagnostics capabilities (both technically and business-wise). What's more, as events contains the business context, it allows business wide analysis and reporting.

Marten's Event Store functionality is a powerful way to utilize Postgresql in the [Event Sourcing](http://martinfowler.com/eaaDev/EventSourcing.html) style of persistence in your application. Beyond simple event capture and access to the raw event stream data, Marten also helps you create "read side" views of the raw event data through its rich support for [projections](/events/projections/).

For a deeper dive into event sourcing, its core concepts, advanced implementation scenarios, and Marten's specific features, be sure to check out our comprehensive guide: [Understanding Event Sourcing with Marten](/events/learning).

## Terminology and Concepts

First, some terminology that we're going to use throughout this section:

* _Event_ - a persisted business event representing a change in state or record of an action taken in the system
* _Stream_ - a related "stream" of events representing a single aggregate
* _Aggregate_ - a type of projection that "aggregates" data from multiple events to create a single read-side view document
* _Projection_ - any strategy for generating "read side" views from the raw events
* _Inline Projections_ - a projection that executes "inline" as part of any event capture transaction to build read-side views that are persisted as a document
* _Async Projections_ - a projection that runs in a background process using an [eventual consistency](https://en.wikipedia.org/wiki/Eventual_consistency) strategy, and is stored as a document
* _Live Projections_ - evaluates a projected view from the raw event data on demand within Marten without persisting the created view

## Event Types

The only requirement that Marten makes on types used as events is that they are:

1. Public, concrete types
1. Can be bidirectionally serialized and deserialized with a tool like Newtonsoft.Json

Marten does need to know what the event types are before you issue queries against the event data (it's just to handle the de-serialization from JSON). The event registration will happen automatically when you append events,
but for production usage when you may be querying event data before you append anything, you just need to register the event types upfront like this:

<!-- snippet: sample_registering-event-types -->
<a id='snippet-sample_registering-event-types'></a>
```cs
var store2 = DocumentStore.For(_ =>
{
    _.DatabaseSchemaName = "samples";
    _.Connection(ConnectionSource.ConnectionString);
    _.AutoCreateSchemaObjects = AutoCreate.None;

    _.Events.AddEventType(typeof(QuestStarted));
    _.Events.AddEventType(typeof(MonsterSlayed));
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/schema_object_management.cs#L41-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering-event-types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Stream or Aggregate Types

At this point there are no specific requirements about stream aggregate types as they are purely marker types. In the future we will probably support aggregating events via snapshot caching using the aggregate type.
