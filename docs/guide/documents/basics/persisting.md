# Storing Documents and Unit Of Work

At this point, the `IDocumentSession` is the sole [unit of work](http://martinfowler.com/eaaCatalog/unitOfWork.html) for transactional updates -- but that may change later as Marten outgrows its origin as a replacement for RavenDb. As [shown before](/guide/documents/), document sessions come in three flavors (lightweight, identity map tracking, and identity map + dirty checking), but there are only two modes of change tracking:

1. Lightweight and the standard "identity map" sessions require users to do all the change tracking manually and tell the `IDocumentSession`
   what documents have changed
1. The "dirty checking" session tries to determine which documents loaded from that `IDocumentSession` has any changes when `IDocumentSession.SaveChanges()` is called

::: tip INFO
When using a `Guid`/`CombGuid`, `Int`, or `Long` identifier, Marten will ensure the identity is set immediately after calling `IDocumentSession.Store` on the entity.
:::

## Storing Multiple Documents

The signature of `IDocumentSession.Store()` changed in v0.8 to allow you to specify a params array of one or more documents:

<!-- snippet: sample_using-store-with-multiple-docs -->
<a id='snippet-sample_using-store-with-multiple-docs'></a>
```cs
user1 = new User {FirstName = "Jeremy"};
user2 = new User {FirstName = "Jens"};
user3 = new User {FirstName = "Jeff"};
user4 = new User {FirstName = "Corey"};

theSession.Store(user1, user2, user3, user4);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_running_through_the_IdentityMap_Tests.cs#L24-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-store-with-multiple-docs' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also store a mixed array of different types by using either the `IDocumentSession.StoreObjects()` method or by calling `IDocumentSession.Store<object>(object[])`.

<!-- snippet: sample_mixed-docs-to-store -->
<a id='snippet-sample_mixed-docs-to-store'></a>
```cs
var user1 = new User {FirstName = "Jeremy", LastName = "Miller"};
var issue1 = new Issue {Title = "TV won't turn on"}; // unfortunately true as I write this...
var company1 = new Company{Name = "Widgets, inc."};
var company2 = new Company{Name = "BigCo"};
var company3 = new Company{Name = "SmallCo"};

theSession.Store<object>(user1, issue1, company1, company2, company3);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/persist_and_deleting_multiple_documents_Tests.cs#L18-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_mixed-docs-to-store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Insert & Update

While `IDocumentSession.Store` will perform either insertion or update depending on the existence of documents, `IDocumentSession` also exposes methods to explicitly control this persistence behavior through `IDocumentSession.Insert` and `IDocumentSession.Update`.

`IDocumentSession.Insert` stores a document only in the case that it does not already exist. Otherwise a `DocumentAlreadyExistsException` is thrown. `IDocumentSession.Update` on the other hand performs an update on an existing document or throws a `NonExistentDocumentException` in case the document cannot be found.

<!-- snippet: sample_sample-document-insertonly -->
<a id='snippet-sample_sample-document-insertonly'></a>
```cs
using (var session = theStore.OpenSession())
{
    session.Insert(target);
    session.SaveChanges();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/document_inserts.cs#L81-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-document-insertonly' title='Start of snippet'>anchor</a></sup>
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

Do note that Marten's .Net API makes no distinctions between inserts and updates. The Postgresql functions generated by Marten to update the document storage tables perform "upserts" for you. Anytime a document is registered through `IDocumentSession.Store(document)`, Marten runs the "auto-assignment" policy for the id type of that document. See [identity](/guide/documents/identity/) for more information on document id's.


## Automatic Dirty Tracking Session

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

## SaveChanges() Optimization

The call to `IDocumentSession.SaveChanges()` tries to batch all the queued updates and deletes into a single ADO.Net call to PostgreSQL. Our testing has
shown that this technique is much faster than issuing one ADO.Net call at a time.

## Bulk Inserts

See also [bulk insert](/guide/documents/basics/bulk-insert) for an alternative for inserting a large number of one kind of document at a time. Also see the section on [identity map](/guide/documents/advanced/identity-map).
