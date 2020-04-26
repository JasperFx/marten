# Marten v4 Ideas

## Pulling it Off

We've got the typical problems of needing to address incoming pull requests and bug issues in master while probably needing to have a long lived branch for *v4*. 

Here are some thoughts:

* First do a bug sweep 3.12 release to address as many of the tactical problems as we can before branching to v4?
* Start with the unit testing improvements as a way to speed up the build before we dive into much more of this? 
* Possibly do a series of v4, then v5 releases to do this in smaller chunks? We've mostly said do the event store as v4, then Linq improvements as v5
* Do the Event Store v4 work in a separate project built as an add on from the very beginning, but leave the existing event store in place. That would enable us to do a lot of work *and* mostly be able to keep that work in master so we don't have long-lived branch problems
* Damn the torpedos and full speed ahead!

## Miscellaneous Ideas

* Look at some kind of object pooling for the `DocumentSession` / `QuerySession` objects?
* Ditch the document by document type schema configuration. Do that, and I think we open the door for multi-tenancy by schema
* Eliminate `ManagedConnection` altogether. I think it results in unnecessary object allocations and it's causing more harm that help as it's been extended over time
* Can we consider ditching < .Net Core or .Net v5 for v4? 


## Dynamic Code Generation

If you look at the [pull request for Document Metadata](https://github.com/JasperFx/marten/pull/1364) and the code in `Marten.Schema.Arguments` you can see that our dynamic `Expression` to `Lambda` compilation code is getting extremely messy, hard to reason with, and difficult to extend.

**Idea**: Introduce a dependency on [LamarCodeGeneration and LamarCompiler](https://jasperfx.github.io/lamar/documentation/compilation/). LamarCodeGeneration has a strong model for dynamically generating C# code at runtime. LamarCompiler adds runtime Roslyn support to compile assemblies on the fly and utilities to attach/create these classes. We *could* stick with `Expression` to `Lambda` compilation, but that can't really handle any kind of asynchronous code without some severe pain and it's far more difficult to reason about (Jeremy's note: I'm uniquely qualified to make this statement unfortunately).

What gets dynamically generated today:

* Bulk importer handling for a single entity
* Loading entities and tracking entities in the *identity map* or version tracking

What could be generated in the future:

* Document metadata properties -- but sad trombone, that might have to stay with Expressions if the setters are internal/private :/
* Much more of the `ISelector` implementations, especially since there's going to be more variability when we do the document metadata
* Finer-grained manipulation of the `IIdentityMap` 


## Unit Testing Approach

If we introduce the runtime code generation back into Marten, that's unfortunately a non-trivial "cold start" testing issue. To soften that, I suggest we get a lot more aggressive with reusable [xUnit.Net class fixtures](https://xunit.net/docs/shared-context) between tests to reuse generated code between tests, cut way down on the sheer number of database calls by not having to frequently check the schema configuration, and other `DocumentStore` overhead. 

A couple other points about this:

* We need to create more unique document types so we're not having to use different configurations for the same document type. This would enable more reuse inside the testing runtime
* Be aggressive with separate schemas for different configurations
* We could possibly turn on xUnit.net parallel test running to speed up the test cycles


## Document Metadata

* From the feedback on GitHub, it sounds like the desire to extend the existing metadata to tracking data like correlation identifiers, transaction ids, user ids, etc. To make this data
easy to query on, I would prefer that this data be separate columns in the underlying storage 

* Use the configuration and tests from [pull request for Document Metadata](https://github.com/JasperFx/marten/pull/1364), but use the Lamar-backed dynamic code generation from the previous section to pull this off.

* I strongly suggest using a new dynamic codegen model for the `ISelector` objects that would be responsible for putting Marten's own document metadata like `IsDeleted` or `TenantId` or `Version` onto the resolved objects (but that falls apart if we have to use private setters)
* I think we could expand the document metadata to allow for user defined properties like "user id" or "transaction id" much the same way we'll do for the EventStore metadata. We'd need to think about how we extend the document tables and how metadata is attached to a document session

My thought is to designate one (or maybe a few?) .Net type as the "metadata type" for your application like maybe this one:

```
    public class MyMetadata
    {
        public Guid CorrelationId { get; set; }
        public string UserId { get; set; }
    }
```
Maybe that gets added to the `StoreOptions` something like:

```
var store = DocumentStore.For(x => {
    // other stuff

    // This would direct Marten to add extra columns to
    // the documents and events for the metadata properties
    // on the MyMetadata type.

    // This would probably be a fluent interface to optionally fine tune
    // the storage and applicability -- i.e., to all documents, to events, etc.
    x.UseMetadataType<MyMetadata>();
});
```

Then at runtime, you'd do something like:

```
session.UseMetadata<MyMetadta>(metadata);
```

Either through docs or through the new, official .Net Core integration, we have patterns to have that automatically set upon
new `DocumentSession` objects being created from the IoC to make the tracking be seemless.

## Project / Assembly Restructuring

* Pull everything to do with Schema object generation and difference detection to a separate library (`IFeatureSchema`, `ISchemaObject`, etc.). Mostly to clean out the main library, but also because this code could easily be reused outside of Marten. Separating it out might make it easier to test and extend that functionality, which is something that occasionally gets requested. There's also the possibility of further breaking that into abstractions and implementations for the long run of getting us ready for Sql Server or other database engine support

* *Possibly* pull the ADO.Net helper code like `CommandBuilder` and the extension methods into a small helper library somewhere else (I'm nominating the [Baseline](https://jasperfx.github.io/baseline) repository). This code is copied around to other projects as it is, and it's another way of getting stuff out of the main library and the test suite.

* More in the event store section, but let's get the event store functionality into its own library (or possibly a couple other libraries for async projection support).

## F# Improvements

WE NEED TO HAVE AN F# SUBCOMMITTEE TO HELP HERE. 

## HostBuilder Integration

If he's okay with this, I'd vote to bring Joona-Pekka Kokko's ASP.Net Core integration library into the main repository and make that the officially blessed and documented recipe for integrating Marten into .Net Core applications based on the `HostBuilder` in .Net Core. I suppose we could also multi-target `IWebHostBuilder` for ASP.Net Core 2.*.

That `HostBuilder` integration could be extended to:

* Optionally set up the Async Daemon in an `IHostedService` -- more on this in the Event Store section
* Optionally register some kind of `IDocumentSessionBuilder` that could be used to customize session construction? 
* Have some way to have container resolved `IDocumentSessionListener` objects attached to `IDocumentSession`. This is to have an easy recipe for folks who want events broadcast through messaging infrastructure in CQRS architectures

## Command Line Support

The `Marten.CommandLine` package already uses Oakton for command line parsing. For easier integration in .Net Core applications, we could shift that to using the [Oakton.AspNetCore](https://jasperfx.github.io/oakton/documentation/aspnetcore/) package so the command line support can be added to any ASP.net Core 2.* or .Net Core 3.* project by installing the Nuget. This might simplify the usage because you'd no longer need a separate project for the command line support.

There are some long standing stories about extending the command line support for the event store projection rebuilding. I think that would be more effective if it switches over to Oakton.AspNetCore.


## Linq

This is also covered by the [Linq Overhaul](https://github.com/JasperFx/marten/issues/1201) issue.

* Bring back the work in the `linq` branch for the [revamped IField model](https://github.com/JasperFx/marten/issues/1243) within the Linq provider. This would be advantageous for performance, cleans up some conditional code in the Linq internals, *could* make the Linq support be aware of Json serialization customizations like `[JsonProperty]`, and probably helps us deal more with F# types like discriminated unions.

* Completely rewrite the `Include()` functionality. Use Postgresql [Common Table Expression](https://www.postgresqltutorial.com/postgresql-cte/) and `UNION` queries to fetch both the parent and any related documents in one query without needing to do any kind of `JOIN` s that complicate the selectors. There'd be a column for document type the code could use to switch. The dynamic code generation would help here. This could **finally** knock out the long wished for [Include() on child collections](https://github.com/JasperFx/marten/issues/460) feature. This work would nuke the `InnerJoin` stuff in the `ISelector` implementations, and that would hugely simplify a lot of code.

* Finer grained code generation would let us optimize the interactions with `IdentityMap`. For purely query sessions, you could completely skip any kind of interaction with `IdentityMap` instead of wasting cycles on nullo objects. You could pull out a specific `IdentityMap<TEntity, TKey>` out of the larger identity map just before calling selectors to avoid some repetitive "find the right inner dictionary" on each document resolved.

* Maybe introduce a new concept of `ILinqDialect` where the `Expression` parsing would just detect *what* logical thing it finds (like `!BoolProperty`), and turns around and calls this `ILinqDialect` to get at a `WhereFragment` or whatever. This way we could ready ourselves to support an alternative json/sql dialect around JSONPath for Postgresql v12+ and later for Sql Server vNext. I think this would fit into the theme of making the Linq support more modular. It *should* make the Linq support easier to unit test as we go. Before we do anything with this, let's take a deep look into the EF Core internals and see how they handle this issue

* Consider replacing the `SelectMany()` implementation with *Common Table Expression* sql statements. That might do a lot to simplify the internal mechanics. Could definitely get us to an n-deep model.

* Do the [Json streaming story](https://github.com/JasperFx/marten/issues/585) because it should be compelling, especially as part of the readside of a CQRS architecture using Marten's event store functionality. 

## Dynamic Code Generation

* Adopt LamarCodeGeneration for building out the dynamic code. The current Expression building is wonky, and it's getting creaky. This will make the document metadata functionality
  easier to build. We can do this either by continuing to build Expressions and compiling to Funcs, or we could re-introduce code compilation via LamarCompiler (which was originally
  built into Marten pre-V1)

**The potential downside is that this doesn't work well at all with `internal` properties, so we'll have to watch that**


## Partial Updates

Use native postgresql partial JSON updates wherever possible. Let's do a perf test on that first though.

## Event Sourcing

There's an existing conversation on GitHub about the [event sourcing improvements for v4](https://github.com/JasperFx/marten/issues/1307).

### Event Store Metadata

See the earlier conversation about document metadata up above, and also the next section on schema generation for the event store.

What if we allow for custom event wrapper (what's now `IEvent` and `Event<T>`) types, and event stream types? So you could use custom
metadata information right on those types? And possibly even put some logic into event stream creation to capture stream
metadata?

If so, those types could be something like this:

```
    public enum StreamState
    {
        CreateNew,
        Append,
        AppendOrCreate,
        Pending,
        History
    }
    
    // This really represents a segment of the underlying stream
    public abstract class EventStreamBase<TKey, TEvent, TEventBase> where TEvent : IEvent<TEventBase>
    {
        public TKey Id { get; set; }

        // There's a little bit of optimization for inline
        // projections by doing this

        // Other folks want different mechanics
        public StreamState State { get; set; }

        public IList<TEvent> Events { get; } = new List<TEvent>();


        // Data in here while it's being appended, but moves up above when
        // it's pushed into the database
        public IList<TEventBase> Pending { get; } = new List<TEventBase>();
    }

    public interface IEvent<TEventBase>
    {
        Guid Id { get; set; }
        int Version { get; set; }
        long Sequence { get; set; }
        TEventBase Data { get; set; }
        DateTimeOffset Timestamp { get; set; }

        // Additional, user-defined metadata properties
        // like user id or correlation id or whatever

    }

    // This might be too heavyweight
    public interface IMultitenantEvent<TEventBase>: IEvent<TEventBase>
    {
        string TenantId { get; set; }
    }

The custom stream and event types could be used to define segregations between different types of event logs within a single application as part
of the partitioning/sharding/segmenting strategy



The schema generation would conform to the shape of the base event and stream wrapper much the way that document storage does in the Doc Db features.

### Table/Function Generation and Stream Identity

TODO -- allow more flexibility in the identity of streams, so `int`, `long`, `Guid`, `string` just like the document types. Do more code generation
to accomodate this.

TODO -- there will be more columns & even new tables to track and coordinate the asynchronous projections

### Event Store Partitioning or Sharding

The event sourcing in Marten has limited scalability because of its dependence today upon appending events to 
one table. Closely related to the storage is ways to segment the asynchronous projection support to parallelize
the work across multiple threads or active nodes.

Here are some ideas:

* Allow the user to define multiple event logs in the database. This way you'd have multiple stream/event tables for completely
  different kinds of streams
* Postgresql native partitioning by tenant id where that's useful
* Aggregation keys -- this is tightly coupled to the projections. If there's a projection that is done by something other than tenant id or stream id, try to persist the 
  key for however the events/streams are grouped to stream and/or event storage. Thinking about data elements like "Region" or "Country" where you might be doing
  aggregations across streams or tenants. Maybe that's useful?
* Archive streams that reach some kind of termination event? Move everything to an archive table off to the side? Keeps the event log table cleaner
* Artificial segmenting of the streams. Use some kind of algorithm that could see stream ids, and assign them to 2 or more "cohorts", then persist that "cohort key" to 
  both the stream and event data. This would potentially help for the async daemon to parallelize work. It would definitely help out for "live" projections by having 
  smaller tables to work through

### Projections

Thoughts:

Functionality wise, the lifecycle of projections are:

* "Live" projections built on demand from the raw events. This only changes for v4 to take advantage of new snapshotting capabilities
* "Inline" projections built as part of the same unit of work as capturing the source events. See the section on *Inline Projections* because this
  will change internally for v4
* "Async" projections built up in a background process of some sort. I think this turns into a near re-write for v4

By *type*, the projection types *could* be:

* Aggregate document by stream (existing)
* Aggregate document by tenant id (new)
* Execute SQL statement by event type (new) -- so, insert/update/delete. This comes up a lot, and it should be low hanging fruit
* Transform a single event to a document --> needs to become just "carry out some kind of change to a document" and probably ends up sharing more mechanics with the aggregation than it used to
* Aggregate by arbitrary values of either the stream or event types -- but if we do this, I strongly vote to persist whatever the differentiation value is in the database to make projection rebuilds and the async daemon more "parallelizable"


Other thoughts:

* I want to completely throw away the current `Aggregator` implementations. It was just too naive for real world usage and the internals have gotten ugly
* Re-evaluate the `ViewProjection` fluent interface. That might fit in fine to our new model of projections for v4 with more overloads
* I've been in favor of having adapters for existing projection libraries like [Liquid Projections](https://liquidprojections.net/) in the past, but I think we'd be losing too
  much optization, especially for the aggregations inside the async daemon.

#### Defining a Projection to a Document


```
ProjectTo<ProjectedDocument>()
    // How to transform the events to the projected document, see below
    .TransformWith<SampleTransformer>()

    // Optionally aggregate by stream
    .AggregateByStream() // Think this is self-descriptive

    .AggregateByTenant() // Not sure how common this would be. Aggregate across the entire tenant

    // Or aggregate by something special, hopefull use a common base type here for the event type
    .AggregateByEventData<TEvent>(Expression<Func<TEvent, int>> expression) // what's the identity of the projected, aggregate doc?

    .DeleteOn<TEvent>() // the aggregate document should be deleted if an event of this type hits
                        // In this case, we could look ahead and "know" in the rebuilds not to rebuild
                        // a projection for this aggregate as an optimization

    .SnapshotInIncrementsOf(10) // Then maybe there are other options to retain old snapshots or not? Dunno
    
```


Now that folks are used to the convention based signature approach of `StartUp` in ASP.Net Core, I suggest doing something
similar for the projections like so:

```
    // This purposely does not implement any kind of interface
    // Defines how to apply changes to one kind of projected document
    public class SampleTransformer
    {
        

        // Support constructor injection here for services from the container?
        // Could also support method injection too, w/ maybe something like the ASP.net Core
        // [FromServices] attribute on parameters???

        // Marten should conform to the arguments of the method, so any combination of
        // the actual event (either the specific type or a base type), the event wrapper
        // with metadata, and the stream model. Plus the projected document where appropriate

        // These may not be necessary, but if so, this is to 
        // get at what the identity of the aggregate document is
        // Prefer the FI version of this though
        public int AggregateBy(Event1 @event);
        public int AggregateBy(EventWrapper @event);

        // Sync version with raw event
        public void Apply(Event1 @event, ProjectedDocument doc);

        // Sync version with event wrapper, could be a custom implementation of IEvent
        // to get at event metadata & stream metadata or partitioning information
        public void Apply(EventWrapper<Event1> @event, MyEventStream stream, ProjectedDocument doc);

        // Sync version w/ immutable projected docs
        public ProjectedDocument Apply(Event1 @event, ProjectedDocument doc);

        // apply a partial update to an aggregate document, but this wouldn't
        // get used if there's some reason to fetch the whole aggregate for a segment
        // of events. 
        public void Partial(Event1, @event, IDocumentSession session);

        // Should the aggregate document be deleted based on some event data?
        public bool ShouldBeDeleted(Event1 @event);
        public bool ShouldBeDeleted(EventWrapper @event);
        public bool ShouldBeDeleted(EventWrapper @event, MyEventStream stream);
    }
```

#### Defining a Projection to a SQL Statement(s)

TODO

#### Inline Projections

There's been some complaints and obvious confusion about the `IEvent` interface and how the metadata properties like
`version` aren't available during inline projections. That's because today the inline projections are all calculated 
just prior to submitting the database commands before the version numbers of each event within the stream would be calculated. 

In v4, the inline projection support and the `UpdateBatch` mechanics should be smart enough to "know" when the transformation
or aggregation requires metadata, so that:

#### If no event or stream metadata is necessary...

It works basically as it does today. Upon `IDocumentSession.SaveChanges() / SaveChangesAsync()`:

1. The inline projections work across the incoming event objects, create new projected documents
or update existing documents as necessary just prior to sending the current `UnitOfWork` to the database
1. The `DocumentSession` issues the batched up commands with both the event capture and the projection documents
1. The underlying database transaction is committed

If there's no need for event metadata, do thing in fewer network round trips

#### If event or stream metadata is necessary...

This gets a little more complicated, because the projections can't be calculated until *after* the events are appended, so:

1. The events get appended, and the sproc returns an array of the new event versions
1. Build up an array of `IEvent` wrappers that have the stream and event metadata
1. Pass the `IEvent` wrappers into the transformation code, and get an additional `UpdateBatch` of projected document inserts
   and updates
1. Submit the subsequent `UpdateBatch` to the database
1. The underlying database transaction is committed

The difference here is that this will take multiple batched commands to the database within the same `NpgsqlTransaction`





### Snapshots & "Live" Projections


## Async Daemon

For v4, the Async Daemon needs to come in a couple different possible deployment configurations:

1. "Classic" database polling mode -- no queueing, just something that polls for new events and runs the projections. I think this will be a near rewrite in v4, but there's some 
   existing art in [Jasper's DurabilityAgent](http://jasperfx.github.io/documentation/durability/) that might help this along
1. Messaging-based Mode -- use Rabbit MQ, Azure Service Bus, Kafka, etc. to gather up events and connect with the running projections




### Rebuilding Projections



