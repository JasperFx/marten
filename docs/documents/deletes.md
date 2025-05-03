# Deleting Documents

You can register document deletions with an active `IDocumentSession` by either the document itself or just by the document id to avoid having to fetch
a document from the database just to turn around and delete it. Keep in mind that using any of the methods
around deleting a document or specifying a criteria for deleting documents in an `IDocumentSession`,
you're really just queueing up a pending operation to the current `IDocumentSession` that is executed
in a single database transaction by calling the `IDocumentSession.SaveChanges()/SaveChangesAsync()` method.

As explained later in this page, Marten supports both "hard" deletes where the underlying database row is permanently deleted
and "soft" deletes where the underlying database row is just marked as deleted with a timestamp.

## Delete a Single Document by Id

A single document can be deleted by either telling Marten the identity and the document type
as shown below:

<!-- snippet: sample_delete_by_document_id -->
<a id='snippet-sample_delete_by_document_id'></a>
```cs
internal Task DeleteByDocumentId(IDocumentSession session, Guid userId)
{
    // Tell Marten the type and identity of a document to
    // delete
    session.Delete<User>(userId);

    return session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Deletes.cs#L10-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_delete_by_document_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Delete by Document

If you already have a document in memory and determine that you want that document to be deleted, you can pass that document directly to `IDocumentSession.Delete<T>(T document)` as shown below:

<!-- snippet: sample_delete_by_document -->
<a id='snippet-sample_delete_by_document'></a>
```cs
public Task DeleteByDocument(IDocumentSession session, User user)
{
    session.Delete(user);
    return session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Deletes.cs#L69-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_delete_by_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Delete by Criteria

Marten also provides the ability to delete any documents of a certain type meeting a Linq expression using the `IDocumentSession.DeleteWhere<T>()` method:

<!-- snippet: sample_DeleteWhere -->
<a id='snippet-sample_deletewhere'></a>
```cs
theSession.DeleteWhere<Target>(x => x.Double == 578);

await theSession.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/delete_many_documents_by_query.cs#L30-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deletewhere' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A couple things to note:

1. The actual Sql command to delete documents by a query is not executed until `IDocumentSession.SaveChanges()` is called
1. The bulk delete command runs in the same batched sql command and transaction as any other document updates or deletes
   in the session

## Delete by mixed document types

Documents of mixed or varying types can be deleted using `IDocumentSession.DeleteObjects(IEnumerable<object> documents)` method.

<!-- snippet: sample_DeleteObjects -->
<a id='snippet-sample_deleteobjects'></a>
```cs
// Store a mix of different document types
var user1 = new User { FirstName = "Jamie", LastName = "Vaughan" };
var issue1 = new Issue { Title = "Running low on coffee" };
var company1 = new Company { Name = "ECorp" };

session.StoreObjects(new object[] { user1, issue1, company1 });

await session.SaveChangesAsync();

// Delete a mix of documents types
using (var documentSession = theStore.LightweightSession())
{
    documentSession.DeleteObjects(new object[] { user1, company1 });

    await documentSession.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/deleting_multiple_documents.cs#L60-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_deleteobjects' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Soft Deletes

You can opt into using "soft deletes" for certain document types. Using this option means that documents are
never actually deleted out of the database. Rather, a `mt_deleted` field is marked as true and a `mt_deleted_at`
field is updated with the transaction timestamp. If a document type is "soft deleted," Marten will automatically filter out
documents marked as _deleted_ unless you explicitly state otherwise in the Linq `Where` clause.

### Configuring a Document Type as Soft Deleted

You can direct Marten to make a document type soft deleted by either marking the class with an attribute:

<!-- snippet: sample_SoftDeletedAttribute -->
<a id='snippet-sample_softdeletedattribute'></a>
```cs
[SoftDeleted]
public class SoftDeletedDoc
{
    public Guid Id;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/configuring_mapping_deletion_style.cs#L21-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_softdeletedattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using the fluent interface off of `StoreOptions`:

<!-- snippet: sample_soft-delete-configuration-via-fi -->
<a id='snippet-sample_soft-delete-configuration-via-fi'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Schema.For<User>().SoftDeleted();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/configuring_mapping_deletion_style.cs#L56-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_soft-delete-configuration-via-fi' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With Marten v4.0, you can also opt into soft-deleted mechanics by having your document type implement the Marten `ISoftDeleted`
interface as shown below:

<!-- snippet: sample_implementing_ISoftDeleted -->
<a id='snippet-sample_implementing_isoftdeleted'></a>
```cs
public class MySoftDeletedDoc: ISoftDeleted
{
    // Always have to have an identity of some sort
    public Guid Id { get; set; }

    // Is the document deleted? From ISoftDeleted
    public bool Deleted { get; set; }

    // When was the document deleted? From ISoftDeleted
    public DateTimeOffset? DeletedAt { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/metadata_marker_interfaces.cs#L131-L145' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_implementing_isoftdeleted' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

More on `ISoftDeleted` in a later section on exposing soft-deleted metadata directly
on documents.

Also starting in Marten v4.0, you can also say globally that you want all document types
to be soft-deleted unless explicitly configured otherwise like this:

<!-- snippet: sample_AllDocumentTypesShouldBeSoftDeleted -->
<a id='snippet-sample_alldocumenttypesshouldbesoftdeleted'></a>
```cs
internal void AllDocumentTypesShouldBeSoftDeleted()
{
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection("some connection string");
        opts.Policies.AllDocumentsSoftDeleted();
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Deletes.cs#L36-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_alldocumenttypesshouldbesoftdeleted' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Querying a "Soft Deleted" Document Type

By default, Marten quietly filters out documents marked as deleted from Linq queries as demonstrated
in this acceptance test from the Marten codebase:

<!-- snippet: sample_query_soft_deleted_docs -->
<a id='snippet-sample_query_soft_deleted_docs'></a>
```cs
[Fact]
public async Task query_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    // Deleting 'bar' and 'baz'
    session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
    await session.SaveChangesAsync();

    // no where clause, deleted docs should be filtered out
    session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("foo", "jack");

    // with a where clause
    session.Query<User>().Where(x => x.UserName != "jack")
        .ToList().Single().UserName.ShouldBe("foo");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L293-L319' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_soft_deleted_docs' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_query_soft_deleted_docs-1'></a>
```cs
[Fact]
public async Task query_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    // Deleting 'bar' and 'baz'
    session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
    await session.SaveChangesAsync();

    // no where clause, deleted docs should be filtered out
    session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("foo", "jack");

    // with a where clause
    session.Query<User>().Where(x => x.UserName != "jack")
        .ToList().Single().UserName.ShouldBe("foo");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L983-L1009' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_soft_deleted_docs-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The SQL generated for the first call to `Query<User>()` above would be:

```sql
select d.data ->> 'UserName' from public.mt_doc_user as d where mt_deleted = False order by d.data ->> 'UserName'
```

### Fetching All Documents, Deleted or Not

You can include deleted documents with Marten's `MaybeDeleted()` method in a Linq `Where` clause
as shown in this acceptance tests:

<!-- snippet: sample_query_maybe_soft_deleted_docs -->
<a id='snippet-sample_query_maybe_soft_deleted_docs'></a>
```cs
[Fact]
public async Task query_maybe_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
    await session.SaveChangesAsync();

    // no where clause, all documents are returned
    session.Query<User>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

    // with a where clause, all documents are returned
    session.Query<User>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
        .OrderBy(x => x.UserName)
        .ToList()
        .Select(x => x.UserName)
        .ShouldHaveTheSameElementsAs("bar", "baz", "foo");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L321-L349' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_maybe_soft_deleted_docs' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_query_maybe_soft_deleted_docs-1'></a>
```cs
[Fact]
public async Task query_maybe_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
    await session.SaveChangesAsync();

    // no where clause, all documents are returned
    session.Query<User>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

    // with a where clause, all documents are returned
    session.Query<User>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
        .OrderBy(x => x.UserName)
        .ToList()
        .Select(x => x.UserName)
        .ShouldHaveTheSameElementsAs("bar", "baz", "foo");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L1011-L1039' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_maybe_soft_deleted_docs-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Partitioning by Deleted Status <Badge type="tip" text="7.26" />

::: info
With this option, Marten separates soft deleted documents into a separate, partitioned table so that the default
querying for not deleted documents can work only against the now smaller, active table partition.
:::

Marten can utilize [PostgreSQL table partitioning](https://www.postgresql.org/docs/current/ddl-partitioning.html) with soft deleted document storage as a way to improve performance
when querying primarily against either the "hot" storage documents (not deleted) or against the "cold" deleted documents
by separating data into partitioned tables. In all cases, this is an _opt in_ configuration that you must explicitly choose
as shown below:

<!-- snippet: sample_soft_deletes_with_partitioning -->
<a id='snippet-sample_soft_deletes_with_partitioning'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Opt into partitioning for one document type
    opts.Schema.For<User>().SoftDeletedWithPartitioning();

    // Opt into partitioning and and index for one document type
    opts.Schema.For<User>().SoftDeletedWithPartitioningAndIndex();

    // Opt into partitioning for all soft-deleted documents
    opts.Policies.AllDocumentsSoftDeletedWithPartitioning();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L1416-L1432' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_soft_deletes_with_partitioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The attribute style of configuration also supports partitioning like so:

<!-- snippet: sample_soft_deleted_attribute_with_partitioning -->
<a id='snippet-sample_soft_deleted_attribute_with_partitioning'></a>
```cs
[SoftDeleted(UsePartitioning = true)]
public class SoftDeletedAndPartitionedDocument
{
    public Guid Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L1447-L1455' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_soft_deleted_attribute_with_partitioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Fetching Only Deleted Documents

You can also query for only documents that are marked as deleted with Marten's `IsDeleted()` method
as shown below:

<!-- snippet: sample_query_is_soft_deleted_docs -->
<a id='snippet-sample_query_is_soft_deleted_docs'></a>
```cs
[Fact]
public async Task query_is_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
    await session.SaveChangesAsync();

    // no where clause
    session.Query<User>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("bar", "baz");

    // with a where clause
    session.Query<User>().Where(x => x.UserName != "baz" && x.IsDeleted())
        .OrderBy(x => x.UserName)
        .ToList()
        .Select(x => x.UserName)
        .Single().ShouldBe("bar");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L351-L379' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_is_soft_deleted_docs' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_query_is_soft_deleted_docs-1'></a>
```cs
[Fact]
public async Task query_is_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
    await session.SaveChangesAsync();

    // no where clause
    session.Query<User>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("bar", "baz");

    // with a where clause
    session.Query<User>().Where(x => x.UserName != "baz" && x.IsDeleted())
        .OrderBy(x => x.UserName)
        .ToList()
        .Select(x => x.UserName)
        .Single().ShouldBe("bar");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L1041-L1069' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_is_soft_deleted_docs-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Fetching Documents Deleted Before or After a Specific Time

To search for documents that have been deleted before a specific time use Marten's `DeletedBefore(DateTimeOffset)` method
and the counterpart `DeletedSince(DateTimeOffset)` as show below:

<!-- snippet: sample_query_soft_deleted_since -->
<a id='snippet-sample_query_soft_deleted_since'></a>
```cs
[Fact]
public async Task query_is_soft_deleted_since_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    session.Delete(user3);
    await session.SaveChangesAsync();

    var epoch = (await session.MetadataForAsync(user3)).DeletedAt;
    session.Delete(user4);
    await session.SaveChangesAsync();

    session.Query<User>().Where(x => x.DeletedSince(epoch.Value)).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("jack");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L381-L405' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_soft_deleted_since' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_query_soft_deleted_since-1'></a>
```cs
[Fact]
public async Task query_is_soft_deleted_since_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using var session = theStore.LightweightSession();
    session.Store(user1, user2, user3, user4);
    await session.SaveChangesAsync();

    session.Delete(user3);
    await session.SaveChangesAsync();

    var epoch = (await session.MetadataForAsync(user3)).DeletedAt;
    session.Delete(user4);
    await session.SaveChangesAsync();

    session.Query<User>().Where(x => x.DeletedSince(epoch.Value)).Select(x => x.UserName)
        .ToList().ShouldHaveTheSameElementsAs("jack");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Deleting/soft_deletes.cs#L1071-L1095' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_soft_deleted_since-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

_Neither `DeletedSince` nor `DeletedBefore` are inclusive searches as shown_below:

<!-- snippet: sample_AllDocumentTypesShouldBeSoftDeleted -->
<a id='snippet-sample_alldocumenttypesshouldbesoftdeleted'></a>
```cs
internal void AllDocumentTypesShouldBeSoftDeleted()
{
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection("some connection string");
        opts.Policies.AllDocumentsSoftDeleted();
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Deletes.cs#L36-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_alldocumenttypesshouldbesoftdeleted' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Undoing Soft-Deleted Documents

New in Marten v4.0 is a mechanism to mark any soft-deleted documents matching a supplied criteria
as not being deleted. The only usage so far is using a Linq expression as shown below:

<!-- snippet: sample_UndoDeletion -->
<a id='snippet-sample_undodeletion'></a>
```cs
internal Task UndoDeletion(IDocumentSession session, Guid userId)
{
    // Tell Marten the type and identity of a document to
    // delete
    session.UndoDeleteWhere<User>(x => x.Id == userId);

    return session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Deletes.cs#L23-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_undodeletion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Explicit Hard Deletes

New in v4.0 is the ability to force Marten to perform hard deletes even on document types
that are normally soft-deleted:

<!-- snippet: sample_HardDeletes -->
<a id='snippet-sample_harddeletes'></a>
```cs
internal void ExplicitlyHardDelete(IDocumentSession session, User document)
{
    // By document
    session.HardDelete(document);

    // By type and identity
    session.HardDelete<User>(document.Id);

    // By type and criteria
    session.HardDeleteWhere<User>(x => x.Roles.Contains("admin"));

    // And you still have to call SaveChanges()/SaveChangesAsync()
    // to actually perform the operations
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/Deletes.cs#L49-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_harddeletes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Deletion Metadata on Documents

The easiest way to expose the metadata about whether or not a document is deleted
and when it was deleted is to implement the `ISoftDeleted` interface as shown
in this sample document:

<!-- snippet: sample_implementing_ISoftDeleted -->
<a id='snippet-sample_implementing_isoftdeleted'></a>
```cs
public class MySoftDeletedDoc: ISoftDeleted
{
    // Always have to have an identity of some sort
    public Guid Id { get; set; }

    // Is the document deleted? From ISoftDeleted
    public bool Deleted { get; set; }

    // When was the document deleted? From ISoftDeleted
    public DateTimeOffset? DeletedAt { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/metadata_marker_interfaces.cs#L131-L145' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_implementing_isoftdeleted' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Implementing `ISoftDeleted` on your document means that:

* The `IsDeleted` and `DeletedAt` properties will reflect the database state any time
  you load a document of a type that is configured as soft-deleted
* Those same properties will be updated when you delete a document that is in memory
  if you call `IDocumentSession.Delete<T>(T document)`

Any document type that implements `ISoftDeleted` will automatically be configured as
soft-deleted by Marten when a `DocumentStore` is initialized.

Now, if you don't want to couple your document types to Marten by implementing that interface,
you're still in business. Let's say you have this document type:

<!-- snippet: sample_ASoftDeletedDoc -->
<a id='snippet-sample_asoftdeleteddoc'></a>
```cs
public class ASoftDeletedDoc
{
    // Always have to have an identity of some sort
    public Guid Id { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedWhen { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/metadata_marker_interfaces.cs#L147-L159' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_asoftdeleteddoc' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can manually -- and independently -- map the `IsDeleted` and `DeletedWhen` properties
on your document type to the Marten metadata like this:

<!-- snippet: sample_manually_wire_soft_deleted_metadata -->
<a id='snippet-sample_manually_wire_soft_deleted_metadata'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.Schema.For<ASoftDeletedDoc>().Metadata(m =>
    {
        m.IsSoftDeleted.MapTo(x => x.IsDeleted);
        m.SoftDeletedAt.MapTo(x => x.DeletedWhen);
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/metadata_marker_interfaces.cs#L21-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_manually_wire_soft_deleted_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
