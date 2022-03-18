
# Opening Sessions

`IDocumentStore` is the root of Marten usage, but most Marten usage in code will
start with one of the session types that can be created from an `IDocumentStore`. The following
diagram explains the relationship between the different flavors of session and the root store:

![DocumentStore and Session Types](/images/DocumentStore.png)

While there are sections below describing each session in more detail, at a high level the different
types of sessions are:

|**Creation**|**Read/Write**|**Identity Map**|**Dirty Checking**|
|------------|--------------|----------------|------------------|
|`IDocumentStore.QuerySession()`|Read Only|No|No|
|`IDocumentStore.OpenSession()`|Read/Write|Yes|No|
|`IDocumentStore.DirtyTrackedSession()`|Read/Write|Yes|Yes|
|`IDocumentStore.LightweightSession()`|Read/Write|No|No|

## Read Only QuerySession

For strictly read-only querying, the `QuerySession` is a lightweight session that is optimized
for reading. The `IServiceCollection.AddMarten()` configuration will set up a DI registration for
`IQuerySession`, so you can inject that into classes like this sample MVC controller:

<!-- snippet: sample_GetIssueController -->
<a id='snippet-sample_getissuecontroller'></a>
```cs
public class GetIssueController: ControllerBase
{
    private readonly IQuerySession _session;

    public GetIssueController(IQuerySession session)
    {
        _session = session;
    }

    [HttpGet("/issue/{issueId}")]
    public Task<Issue> Get(Guid issueId)
    {
        return _session.LoadAsync<Issue>(issueId);
    }

    [HttpGet("/issue/fast/{issueId}")]
    public Task GetFast(Guid issueId)
    {
        return _session.Json.WriteById<Issue>(issueId, HttpContext);
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/AspNetCoreWithMarten/IssueController.cs#L55-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_getissuecontroller' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you have an `IDocumentStore` object though, you can open a query session like this:

<!-- snippet: sample_opening_querysession -->
<a id='snippet-sample_opening_querysession'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
});

using var session = store.QuerySession();

var badIssues = await session.Query<Issue>()
    .Where(x => x.Tags.Contains("bad"))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/OpeningAQuerySession.cs#L11-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_opening_querysession' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Identity Map Mechanics

**Identity Map:**

> Ensures that each object gets loaded only once by keeping every loaded object in a map. Looks up objects using the map when referring to them.
>
>-- <cite>[Martin Fowler](http://martinfowler.com/eaaCatalog/identityMap.html)</cite>

Marten's `IDocumentSession` implements the [_Identity Map_](https://en.wikipedia.org/wiki/Identity_map_pattern) pattern that seeks to cache documents loaded by id. This behavior can be very valuable, for example, in handling web requests or service bus messages when many different objects or functions may need to access the same logical document. Using the identity map mechanics allows the application to easily share data and avoid the extra database access hits -- as long as the `IDocumentSession` is scoped to the web request.

<!-- snippet: sample_using-identity-map -->
<a id='snippet-sample_using-identity-map'></a>
```cs
public void using_identity_map()
{
    var user = new User { FirstName = "Tamba", LastName = "Hali" };
    theStore.BulkInsert(new[] { user });

    // Open a document session with the identity map
    using (var session = theStore.OpenSession())
    {
        session.Load<User>(user.Id)
            .ShouldBeTheSameAs(session.Load<User>(user.Id));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/IdentityMapTests.cs#L8-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-identity-map' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that using the identity map functionality can be wasteful if you aren't able to take advantage of the identity map caching in a session. In those cases, you may want to either use the `IDocumentStore.LightweightSession()` which forgos the identity map functionality, or use the read only `IQuerySession` alternative. RavenDb users will note that Marten does not (yet) support any notion of `Evict()` to manually remove documents from identity map tracking to avoid memory usage problems. Our hope is that the existence of the lightweight session and the read only interface will alleviate the memory explosion problems that you can run into with naive usage of identity maps or the dirty checking when fetching a large number of documents.

The Identity Map functionality is applied to all documents loaded by Id or Linq queries with `IQuerySession/IDocumentSession.Query<T>()`. **Documents loaded by user-supplied SQL in the `IQuerySession.Query<T>(sql)` mechanism bypass the Identity Map functionality.**

## Ejecting Documents from a Session

If for some reason you need to completely remove a document from a session's [identity map](/documents/identity) and [unit of work tracking](/documents/sessions), as of Marten 2.4.0 you can use the
`IDocumentSession.Eject<T>(T document)` syntax shown below in one of the tests:

<!-- snippet: sample_ejecting_a_document -->
<a id='snippet-sample_ejecting_a_document'></a>
```cs
[Fact]
public void demonstrate_eject()
{
    var target1 = Target.Random();
    var target2 = Target.Random();

    using (var session = theStore.OpenSession())
    {
        session.Store(target1, target2);

        // Both documents are in the identity map
        session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
        session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

        // Eject the 2nd document
        session.Eject(target2);

        // Now that 2nd document is no longer in the identity map
        session.Load<Target>(target2.Id).ShouldBeNull();

        session.SaveChanges();
    }

    using (var session = theStore.QuerySession())
    {
        // The 2nd document was ejected before the session
        // was saved, so it was never persisted
        session.Load<Target>(target2.Id).ShouldBeNull();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/SessionMechanics/ejecting_documents.cs#L11-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ejecting_a_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Ejecting all pending changes from a Session

If you want to remove all queued operations such as document changes or event operations in an unit of work, you can use `IDocumentSession.EjectAllPendingChanges()`. Note that calling this method will not impact any existing identity map i.e. all document stores. Here is a sample from one of our tests:

<!-- snippet: sample_ejecting_all_document_changes -->
<a id='snippet-sample_ejecting_all_document_changes'></a>
```cs
[Fact]
public void will_clear_all_document_changes()
{
    theSession.Store(Target.Random());
    theSession.Insert(Target.Random());
    theSession.Update(Target.Random());

    theSession.PendingChanges.Operations().Any().ShouldBeTrue();

    theSession.EjectAllPendingChanges();

    theSession.PendingChanges.Operations().Any().ShouldBeFalse();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/SessionMechanics/ejecting_all_pending_changes.cs#L16-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ejecting_all_document_changes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->




## Connection Handling

Marten uses a single connection to the Postgresql database in each `IQuerySession` or `IDocumentSession`.
The connection is only opened on the first call to the database, but after that remains open until the `IQuerySession`/`IDocumentSession` is disposed. A couple things to note:

* It's imperative that any `IQuerySession`/`IDocumentSession` opened is disposed in order to recover and reuse
  connections to the underlying database
* Because the connection is "sticky" to the session, you can utilize serializable transactions. In the future, Marten will
  also enable you to opt into [locking documents read from the session](https://github.com/JasperFx/marten/issues/356).

There is no place within Marten where it keeps a stateful connection open across sessions.

## Command Timeouts

By default, Marten just uses the underlying timeout configuration from the [Npgsql connection string](http://www.npgsql.org/doc/connection-string-parameters.html).
You can though, opt to set a different command timeout per session with this syntax:

<!-- snippet: sample_ConfigureCommandTimeout -->
<a id='snippet-sample_configurecommandtimeout'></a>
```cs
public void ConfigureCommandTimeout(IDocumentStore store)
{
    // Sets the command timeout for this session to 60 seconds
    // The default is 30
    using (var session = store.OpenSession(new SessionOptions {Timeout = 60}))
    {

    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/SessionMechanics/SessionOptionsTests.cs#L19-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configurecommandtimeout' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Unit of Work Mechanics

::: tip
The call to `IDocumentSession.SaveChanges()` tries to batch all the queued updates and deletes into a single ADO.Net call to PostgreSQL. Our testing has
shown that this technique is much faster than issuing one ADO.Net call at a time.
:::

At this point, the `IDocumentSession` is the sole [unit of work](http://martinfowler.com/eaaCatalog/unitOfWork.html) for transactional updates -- but that may change later as Marten outgrows its origin as a replacement for RavenDb. As [shown before](/documents/), document sessions come in three flavors (lightweight, identity map tracking, and identity map + dirty checking), but there are only two modes of change tracking:

1. Lightweight and the standard "identity map" sessions require users to do all the change tracking manually and tell the `IDocumentSession`
   what documents have changed
1. The "dirty checking" session tries to determine which documents loaded from that `IDocumentSession` has any changes when `IDocumentSession.SaveChanges()` is called

::: tip INFO
When using a `Guid`/`CombGuid`, `Int`, or `Long` identifier, Marten will ensure the identity is set immediately after calling `IDocumentSession.Store` on the entity.
:::

TODO -- Need to talk about SaveChanges / SaveChangesAsync here!

## Adding Listeners

See [Diagnostics and Instrumentation](/diagnostics) for information about using document session listeners.

## Enlisting in Existing Transactions

Before Marten 2.4.0, a Marten `IDocumentSession` always controlled the lifecycle of its underlying database
connection and transaction boundaries. With the 2.4.0+ release, you can pass in an existing transaction or connection, direct
Marten to enlist in an ambient transaction scope, and even direct Marten on whether or not it owns the transaction boundaries
to override whether or not `SaveChanges/SaveChangesAsync` will commit the underlying transaction.

Do note that the transaction scope enlisting is only available in either the full .Net framework (> .Net 4.6) or applications targeting
Netstandard 2.0.

<!-- snippet: sample_passing-in-existing-connections-and-transactions -->
<a id='snippet-sample_passing-in-existing-connections-and-transactions'></a>
```cs
public void samples(IDocumentStore store, NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    // Use an existing connection, but Marten still controls the transaction lifecycle
    var session1 = store.OpenSession(SessionOptions.ForConnection(connection));

    // Enlist in an existing Npgsql transaction, but
    // choose not to allow the session to own the transaction
    // boundaries
    var session3 = store.OpenSession(SessionOptions.ForTransaction(transaction));

    // Enlist in the current, ambient transaction scope
    using (var scope = new TransactionScope())
    {
        var session4 = store.OpenSession(SessionOptions.ForCurrentTransaction());
    }

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/SessionMechanics/ability_to_use_an_existing_connection_and_transaction.cs#L33-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_passing-in-existing-connections-and-transactions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Transaction Isolation Level

New in v0.7 is the ability to configure the transaction isolation level when opening a new `IDocumentSession` by
supplying the optional `isolationLevel` argument. As of v0.9.2, the default level is `ReadCommitted`.

As one of the use cases that spawned this feature, say
that you are using the [Saga pattern](https://lostechies.com/jimmybogard/2013/03/21/saga-implementation-patterns-variations/) in a service bus architecture. When handling a message with this pattern, you typically want to load some kind of persisted state for the long running saga, do some work, then persist the updated saga state. If you need to worry about serializing the messages
for a single saga, you might want to use [serializable transactions](https://en.wikipedia.org/wiki/Serializability) like this:

<!-- snippet: sample_serializable-saga-transaction -->
<a id='snippet-sample_serializable-saga-transaction'></a>
```cs
public class MySagaState
{
    public Guid Id;
}

public void execute_saga(IDocumentStore store, Guid sagaId)
{
    // The session below will open its connection and start a
    // serializable transaction
    using (var session = store.DirtyTrackedSession(IsolationLevel.Serializable))
    {
        var state = session.Load<MySagaState>(sagaId);

        // do some work against the saga

        session.SaveChanges();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/SagaStorageExample.cs#L8-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_serializable-saga-transaction' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Manual Change Tracking

The first step is to create a new `DocumentSession` with the `IDocumentStore.LightweightSession()` (or `IDocumentStore.OpenSession()`):

<!-- snippet: sample_lightweight_document_session_uow -->
<a id='snippet-sample_lightweight_document_session_uow'></a>
```cs
public void lightweight_document_session(IDocumentStore store)
{
    using (var session = store.LightweightSession())
    {
        var user = new User { FirstName = "Jeremy", LastName = "Miller" };

        // Manually adding the new user to the session
        session.Store(user);

        var existing = session.Query<User>().Single(x => x.FirstName == "Max");
        existing.Internal = false;

        // Manually marking an existing user as changed
        session.Store(existing);

        // Marking another existing User document as deleted
        session.Delete<User>(Guid.NewGuid());

        // Persisting the changes to the database
        session.SaveChanges();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/UnitOfWorkMechanics.cs#L9-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_lightweight_document_session_uow' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that Marten's `Store()` method makes no distinctions between inserts and updates. The Postgresql functions generated by Marten to update the document storage tables perform "upserts" for you. Anytime a document is registered through `IDocumentSession.Store(document)`, Marten runs the "auto-assignment" policy for the id type of that document. See [identity](/documents/identity) for more information on document id's.

## Automatic Dirty Checking Sessions

In the case an `IDocumentSession` opened with the dirty checking enabled, the session will try to detect changes to any of the documents loaded by that
session. The dirty checking is done by keeping the original JSON fetched from Postgresql and using Newtonsoft.Json to do a node by node comparison of the
JSON representation of the document at the time that `IDocumentSession` is called.

<!-- snippet: sample_tracking_document_session_uow -->
<a id='snippet-sample_tracking_document_session_uow'></a>
```cs
public void tracking_document_session(IDocumentStore store)
{
    using (var session = store.DirtyTrackedSession())
    {
        var user = new User { FirstName = "Jeremy", LastName = "Miller" };

        // Manually adding the new user to the session
        session.Store(user);

        var existing = session.Query<User>().Single(x => x.FirstName == "Max");
        existing.Internal = false;

        // Marking another existing User document as deleted
        session.Delete<User>(Guid.NewGuid());

        // Persisting the changes to the database
        session.SaveChanges();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/UnitOfWorkMechanics.cs#L35-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tracking_document_session_uow' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do be aware that the automated dirty checking comes with some mechanical cost in memory and runtime performance.
