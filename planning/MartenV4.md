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

* Use the configuration and tests from [pull request for Document Metadata](https://github.com/JasperFx/marten/pull/1364), but use the Lamar-backed dynamic code generation from the previous section to pull this off.
* I think we could expand the document metadata to allow for user defined properties like "user id" or "transaction id" much the same way we'll do for the EventStore metadata. We'd need to think about how we extend the document tables and how metadata is attached to a document session


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

## Event Sourcing




## Async Daemon