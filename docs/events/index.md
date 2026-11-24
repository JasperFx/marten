---
description: Use Marten as an event store on PostgreSQL. Full event sourcing implementation with stream appending, projections, async daemon, subscriptions, and multi-tenancy.
---

# Marten as Event Store

## What is Event Sourcing?

Event Sourcing is a design pattern in which results of business operations are stored as a series of events.

It is an alternative way to persist data. In contrast with state-oriented persistence that only keeps the latest version of the entity state, Event Sourcing stores each state change as a separate event.

Thanks for that, no business data is lost. Each operation results in the event stored in the database. That enables extended auditing and diagnostics capabilities (both technically and business-wise). What's more, as events contains the business context, it allows business wide analysis and reporting.

Marten's Event Store functionality is a powerful way to utilize Postgresql in the [Event Sourcing](http://martinfowler.com/eaaDev/EventSourcing.html) style of persistence in your application. Beyond simple event capture and access to the raw event stream data, Marten also helps you create "read side" views of the raw event data through its rich support for [projections](/events/projections/).

For a deeper dive into event sourcing, its core concepts, advanced implementation scenarios, and Marten's specific features, be sure to check out our comprehensive guide: [Understanding Event Sourcing with Marten](/events/learning).

Much of Marten's event store is built on store-neutral abstractions in `JasperFx.Events` that Polecat and Fisher share. To write code against those abstractions, or to prototype on the in-memory store before you choose one, see [Event Store Abstractions](https://shared-libs.jasperfx.net/events/) in the JasperFx documentation.

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
1. Can be bidirectionally serialized and deserialized by the store's [JSON serializer](/configuration/json): System.Text.Json by default, or Newtonsoft.Json through the opt-in `Marten.Newtonsoft` package

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/schema_object_management.cs#L43-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering-event-types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Union event types

On `net11.0`, an event can be a C# 15 union. Marten stores an appended union as its active case, so the stored event
is, say, a `Deposited` rather than an `AccountEvent`. See [union event types](/events/projections/conventions#union-event-types)
for how projections handle them.

## Stream or Aggregate Types

There are no specific requirements on the stream or aggregate type, the `T` in `StartStream<T>()`, which is stored as
the stream's type in `mt_streams`. Marten does use it:
[`UseMandatoryStreamTypeDeclaration`](/events/appending#mandatory-stream-types) requires it, the
[optimized projection rebuilds](/events/projections/rebuilding#optimized-projection-rebuilds) rely on it, and an
asynchronous projection can filter the events it loads by it.
