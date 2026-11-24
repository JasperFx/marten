# Storing Documents

The primary function of Marten is to store and retrieve documents to a Postgresql database, so here's
all the ways that Marten enables you to write to the document storage.

## "Upsert" with Store()

Postgresql has an efficient _[upsert](https://wiki.postgresql.org/wiki/UPSERT)_ capability that we
exploit in Marten to let users just say "this document has changed" with the `IDocumentSession.Store()`
method and not have to worry about whether or not the document is brand new or is replacing
a previously persisted document with the same identity. Here's that method in action
with a sample that shows storing both a brand new document and a modified document:

<!-- snippet: sample_using_documentsession_store -->
<a id='snippet-sample_using_documentsession_store'></a>
```cs
using var store = DocumentStore.For("some connection string");

await using var session = store.LightweightSession();

var newUser = new User
{
    UserName = "travis.kelce"
};

var existingUser = await session.Query<User>()
    .SingleAsync(x => x.UserName == "patrick.mahomes");

existingUser.Roles = new[] {"admin"};

// We're storing one brand new document, and one
// existing document that will just be replaced
// upon SaveChangesAsync()
session.Store(newUser, existingUser);

await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/StoringDocuments.cs#L37-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_documentsession_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Store()` method can happily take a mixed bag of document types at one time, but you'll need to tell Marten to use `Store<object>()` instead of letting it infer the document type as shown below:

<!-- snippet: sample_store_mixed_bag_of_document_types -->
<a id='snippet-sample_store_mixed_bag_of_document_types'></a>
```cs
using var store = DocumentStore.For("some connection string");
var user1 = new User();
var user2 = new User();
var issue1 = new Issue();
var issue2 = new Issue();
var company1 = new Company();
var company2 = new Company();

await using var session = store.LightweightSession();

session.Store<object>(user1, user2, issue1, issue2, company1, company2);
await session.SaveChangesAsync();

// Or this usage:
var documents = new object[] {user1, user2, issue1, issue2, company1, company2};

// The argument here is any kind of IEnumerable<object>
session.StoreObjects(documents);
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/StoringDocuments.cs#L10-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_store_mixed_bag_of_document_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip Numerically revisioned documents
For a document with a numeric revision (`IRevisioned`, `ILongVersioned`, a `[Version]` member or a mapped revision),
`Store()` is not an unconditional upsert. Since Marten 10 it checks the revision the document carries, so storing a
document you just loaded, unchanged, throws a `ConcurrencyException`. See
[Numeric Revisioned Documents](/documents/concurrency#numeric-revisioned-documents).
:::

## Insert & Update

While `IDocumentSession.Store` will perform either insertion or update depending on the existence of documents, `IDocumentSession` also exposes methods to explicitly control this persistence behavior through `IDocumentSession.Insert` and `IDocumentSession.Update`.

`IDocumentSession.Insert` stores a document only in the case that it does not already exist. Otherwise a `DocumentAlreadyExistsException` is thrown. `IDocumentSession.Update` on the other hand performs an update on an existing document or throws a `NonExistentDocumentException` in case the document cannot be found.

<!-- snippet: sample_sample-document-insertonly -->
<a id='snippet-sample_sample-document-insertonly'></a>
```cs
using (var session = theStore.LightweightSession())
{
    session.Insert(target);
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/document_inserts.cs#L79-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-document-insertonly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`InsertObjects()` is the `Insert()` counterpart of `StoreObjects()`, for a mixed bag of document types:

<!-- snippet: sample_docs_insert_objects -->
<a id='snippet-sample_docs_insert_objects'></a>
```cs
var documents = new object[]
{
    new ApiDocsOrder { Customer = "Wanda" },
    new ApiDocsCoffeeShop { Name = "Dominican Joe's" }
};

await using var session = store.LightweightSession();

// Insert() for a mixed bag of document types: SaveChangesAsync() throws
// DocumentAlreadyExistsException if any of them already exists
session.InsertObjects(documents);
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L259-L274' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_insert_objects' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Adding Your Own Operations to the Unit of Work

`SaveChangesAsync()` sends every queued operation in one batch and one transaction. For plain SQL, use
[`QueueSqlCommand()`](/documents/execute-custom-sql). When you need more control, implement
`Marten.Internal.Operations.IStorageOperation` and queue it with `IDocumentOperations.QueueOperation()`. Marten calls
`ConfigureCommand()` to write the statement into the batch, and an operation whose statement returns rows reads them in
`PostprocessAsync()`:

<!-- snippet: sample_docs_custom_storage_operation -->
<a id='snippet-sample_docs_custom_storage_operation'></a>
```cs
// Deletes every ApiDocsOrder that has not been modified since the cutoff, as one more
// statement in the SaveChangesAsync() batch
public class PurgeStaleOrders(DateTimeOffset cutoff): Marten.Internal.Operations.IStorageOperation, NoDataReturnedCall
{
    public void ConfigureCommand(Weasel.Postgresql.ICommandBuilder builder, IStorageSession session)
    {
        var table = session.StorageFor(DocumentType).TableName.QualifiedName;
        builder.Append($"delete from {table} where mt_last_modified < ");
        builder.AppendParameter(cutoff);
    }

    // Marten makes sure the table for this document type exists before the batch runs
    public Type DocumentType => typeof(ApiDocsOrder);

    public OperationRole Role() => OperationRole.Other;

    // A delete returns no result set, which NoDataReturnedCall tells the batch reader
    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L96-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_custom_storage_operation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample_docs_queue_operation -->
<a id='snippet-sample_docs_queue_operation'></a>
```cs
await using var session = store.LightweightSession();

session.Store(order);
session.QueueOperation(new PurgeStaleOrders(DateTimeOffset.UtcNow.AddYears(-1)));

// Both run in the same batch and the same transaction
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L279-L289' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_queue_operation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A few rules for an operation of your own:

* Write one statement, and use `AppendParameter()` for every value rather than putting it into the SQL text.
* If the statement returns no result set, also implement the `NoDataReturnedCall` marker interface from
  `Weasel.Storage`. The batch reader relies on it to know which operations have a result to read. Don't put it on an
  operation that does return rows: every later operation in the batch would then read a result set meant for another one.
* `DocumentType` tells Marten which document storage the operation touches, so the storage for that type exists before
  the batch runs.

## Bulk Loading

Marten supports [Postgresql's COPY](http://www.postgresql.org/docs/9.4/static/sql-copy.html) functionality for very efficient insertion of documents like you might need for test data set up or data migrations. Marten calls this feature "Bulk Insert," and it's exposed off the `IDocumentStore` interface as shown below:

<!-- snippet: sample_using_bulk_insert -->
<a id='snippet-sample_using_bulk_insert'></a>
```cs
// This is just creating some randomized
// document data
var data = Target.GenerateRandomData(100).ToArray();

// Load all of these into a Marten-ized database
await theStore.BulkInsertAsync(data, batchSize: 500);

// And just checking that the data is actually there;)
(await theSession.Query<Target>().CountAsync()).ShouldBe(data.Length);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L94-L104' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_bulk_insert' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The bulk insert is done with a single transaction. For really large document collections, you may need to page the calls to `IDocumentStore.BulkInsertAsync()`.

::: tip
All bulk insert APIs are asynchronous. If you are concerned with server resource utilization,
prefer the batched overloads shown above.
:::

### Bulk Loading Inside an Existing Transaction

`IDocumentStore.BulkInsertEnlistTransactionAsync()` enlists the bulk insert in a `System.Transactions.Transaction`
instead of committing a transaction of its own, typically the ambient one from a `TransactionScope`. The documents are
committed only if that transaction commits:

<!-- snippet: sample_docs_bulk_insert_enlist_transaction -->
<a id='snippet-sample_docs_bulk_insert_enlist_transaction'></a>
```cs
using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    // Other work enlisted in the same ambient transaction goes here

    await store.BulkInsertEnlistTransactionAsync(orders, Transaction.Current!);

    // Nothing is committed unless the scope completes
    scope.Complete();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L294-L306' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_bulk_insert_enlist_transaction' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It takes the same `BulkInsertMode` and `batchSize` arguments as `BulkInsertAsync()`, and loads into the default tenant
only; there is no overload that takes a tenant id.

By default, bulk insert will fail if there are any duplicate id's between the documents being inserted and the existing database data. You can alter this behavior through the `BulkInsertMode` enumeration as shown below:

<!-- snippet: sample_bulkinsertmode_usages -->
<a id='snippet-sample_bulkinsertmode_usages'></a>
```cs
// Just say we have an array of documents we want to bulk insert
var data = Target.GenerateRandomData(100).ToArray();

using var store = DocumentStore.For("some connection string");

// Discard any documents that match the identity of an existing document
// in the database
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.IgnoreDuplicates);

// This is the default mode, the bulk insert will fail if any duplicate
// identities with existing data or within the data set being loaded are detected
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.InsertsOnly);

// Overwrite any existing documents with the same identity as the documents
// being loaded
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.OverwriteExisting);

// Overwrite any existing documents when the expected version matches
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.OverwriteIfVersionMatches);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L331-L353' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulkinsertmode_usages' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The bulk insert feature can also be used with multi-tenanted documents, but in that
case you are limited to only loading documents to a single tenant at a time as
shown below:

<!-- snippet: sample_multitenancywithbulkinsert -->
<a id='snippet-sample_multitenancywithbulkinsert'></a>
```cs
// Just say we have an array of documents we want to bulk insert
var data = Target.GenerateRandomData(100).ToArray();

using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
    opts.Policies.AllDocumentsAreMultiTenanted();
});

// If multi-tenanted
await store.BulkInsertDocumentsAsync("a tenant id", data);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L364-L378' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multitenancywithbulkinsert' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Bulk Loading with Expected Versions <Badge type="tip" text="8.19" />

There is also a bulk insert mode that will only overwrite the documents _only if_ the version of the document being updated
matches the version being supplied:

<!-- snippet: sample_bulk_insert_with_version_matches -->
<a id='snippet-sample_bulk_insert_with_version_matches'></a>
```cs
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.OverwriteIfVersionMatches);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L355-L359' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bulk_insert_with_version_matches' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

See [the documentation on optimistic concurrency and document versioning](/documents/concurrency) for more information.
