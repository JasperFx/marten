# Optimistic Concurrency

Marten allows you to opt into enforcing offline optimistic concurrency checks against documents that you are attempting to persist. You would use this feature if you're concerned 
about a document in your current session having been modified by another session since you originally loaded the document or issued a command against a now
obsolete version of the original document.

I first learned about this concept from Martin Fowler's [PEAA book](http://martinfowler.com/eaaCatalog/). From [Fowler's definition](http://martinfowler.com/eaaCatalog/optimisticOfflineLock.html), offline optimistic concurrency:

> Prevents conflicts between concurrent business transactions by detecting a conflict and rolling back the transaction.

As of 7.0, Marten has two mechanisms for applying optimistic versioning to documents:

1. The original optimistic concurrency protection that uses a `Guid` as the Marten assigned version
2. "Revisioned" documents that use an integer version tracked by Marten to designate the current version of the document

Note that these two modes are exclusionary and cannot be combined.

::: tip
Optimistic concurrency or the newer revisioned documents are both "opt in" feature in Marten meaning that this is not
enabled by default -- with the exception case being that all projected aggregation documents are automatically marked
as being revisioned
:::

## Guid Versioned Optimistic Concurrency

In Marten's case, you have to explicitly opt into optimistic versioning for each document type. You can do that with either an attribute on your document type like so:

<!-- snippet: sample_UseOptimisticConcurrencyAttribute -->
<a id='snippet-sample_useoptimisticconcurrencyattribute'></a>
```cs
[UseOptimisticConcurrency]
public class CoffeeShop: Shop
{
    // Guess where I'm at as I code this?
    public string Name { get; set; } = "Starbucks";

    public ICollection<Guid> Employees { get; set; } = new List<Guid>();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Concurrency/optimistic_concurrency.cs#L833-L843' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_useoptimisticconcurrencyattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using Marten's configuration API to do it programmatically:

<!-- snippet: sample_configuring-optimistic-concurrency -->
<a id='snippet-sample_configuring-optimistic-concurrency'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // Adds optimistic concurrency checking to Issue
    _.Schema.For<Issue>().UseOptimisticConcurrency(true);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Concurrency/optimistic_concurrency.cs#L35-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-optimistic-concurrency' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Once optimistic concurrency is turned on for the CoffeeShop document type, a session will now only be able to update a document if the document has been unchanged in the database since it was initially loaded.

To demonstrate the failure case, consider the following  acceptance test from Marten's codebase:

<!-- snippet: sample_update_with_stale_version_standard -->
<a id='snippet-sample_update_with_stale_version_standard'></a>
```cs
[Fact]
public void update_with_stale_version_standard()
{
    var doc1 = new CoffeeShop();
    using (var session = theStore.LightweightSession())
    {
        session.Store(doc1);
        session.SaveChanges();
    }

    var session1 = theStore.DirtyTrackedSession();
    var session2 = theStore.DirtyTrackedSession();

    var session1Copy = session1.Load<CoffeeShop>(doc1.Id);
    var session2Copy = session2.Load<CoffeeShop>(doc1.Id);

    try
    {
        session1Copy.Name = "Mozart's";
        session2Copy.Name = "Dominican Joe's";

        // Should go through just fine
        session2.SaveChanges();

        var ex = Exception<ConcurrencyException>.ShouldBeThrownBy(() =>
        {
            session1.SaveChanges();
        });

        ex.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(Shop).FullName} #{doc1.Id}");
    }
    finally
    {
        session1.Dispose();
        session2.Dispose();
    }

    using (var query = theStore.QuerySession())
    {
        query.Load<CoffeeShop>(doc1.Id).Name.ShouldBe("Dominican Joe's");
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Concurrency/optimistic_concurrency.cs#L127-L171' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_update_with_stale_version_standard' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten is throwing an `AggregateException` for the entire batch of changes.

## Using IVersioned

A new feature in Marten V4 is the `IVersioned` marker interface. If your document type implements this interface as shown below:

<!-- snippet: sample_MyVersionedDoc -->
<a id='snippet-sample_myversioneddoc'></a>
```cs
public class MyVersionedDoc: IVersioned
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/metadata_marker_interfaces.cs#L121-L129' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_myversioneddoc' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Your document type will have the optimistic concurrency checks applied to updates _when_ the current version is given to Marten. Moreover, the current version
will always be written to the `IVersioned.Version` property when the document is modified or loaded by Marten. This makes `IVersioned` an easy strategy to track
the current version of documents in web applications.

## Numeric Revisioned Documents

::: info
This feature was originally introduced to help support asynchronous projection aggregations through the `FetchForWriting()` API. The 
behavior is slightly different than the older Guid-based optimistic concurrency option 
:::

In this newer feature introduced by Marten 7.0, documents can be marked with numeric revisions that can be used to enforce
optimistic concurrency. In this approach, Marten is saving an integer value for the current document revision in the `mt_version`
field. As in the older `Guid` versioned approach, you also have the option to track the current revision on the documents themselves by
designating a public property or field on the document type as the "Version" (the recommended idiom is to just call it `Version`).

You can opt into this behavior on a document by document basis by using the fluent interface
like this:

<!-- snippet: sample_UseNumericRevisions_fluent_interface -->
<a id='snippet-sample_usenumericrevisions_fluent_interface'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Enable numeric document revisioning through the
    // fluent interface
    opts.Schema.For<Incident>().UseNumericRevisions(true);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RevisionedDocuments.cs#L13-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usenumericrevisions_fluent_interface' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

or by implementing the `IRevisioned` interface in a document type:

<!-- snippet: sample_versioned_reservation -->
<a id='snippet-sample_versioned_reservation'></a>
```cs
// By implementing the IRevisioned
// interface, we're telling Marten to
// use numeric revisioning with this
// document type and keep the version number
// on the Version property
public class Reservation: IRevisioned
{
    public Guid Id { get; set; }

    // other properties

    public int Version { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RevisionedDocuments.cs#L83-L99' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_versioned_reservation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
If using the `IRevisioned` interface, or by mapping another property to the version metadata, Marten will pass the 
version number from the document itself such that `IDocumentSession.Store()` is essentially `IDocumentSession.UpdateVersion(entity, entity.Version)`
:::

or finally by adding the `[Version]` attribute to a public member on the document type to opt into the 
`UseNumericRevisions` behavior on the parent type with the decorated member being tracked as the version number as
shown in this sample:

<!-- snippet: sample_versioned_order -->
<a id='snippet-sample_versioned_order'></a>
```cs
public class Order
{
    public Guid Id { get; set; }

    // Marking an integer as the "version"
    // of the document, and making Marten
    // opt this document into the numeric revisioning
    [Version]
    public int Version { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RevisionedDocuments.cs#L68-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_versioned_order' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And here's an attempt to explain the usage and behavior:

<!-- snippet: sample_using_numeric_revisioning -->
<a id='snippet-sample_using_numeric_revisioning'></a>
```cs
public static async Task try_revisioning(IDocumentSession session, Reservation reservation)
{
    // This will create a new document with Version = 1
    session.Insert(reservation);

    // "Store" is an upsert, but if the revisioned document
    // is all new, the Version = 1 after changes are committed
    session.Store(reservation);

    // If Store() is called on an existing document
    // this will just assign the next revision
    session.Store(reservation);

    // *This* operation will enforce the optimistic concurrency
    // The supplied revision number should be the *new* revision number,
    // but will be rejected with a ConcurrencyException when SaveChanges() is
    // called if the version
    // in the database is equal or greater than the supplied revision
    session.UpdateRevision(reservation, 3);

    // This operation will update the document if the supplied revision
    // number is greater than the known database version when
    // SaveChanges() is called, but will do nothing if the known database
    // version is equal to or greater than the supplied revision
    session.TryUpdateRevision(reservation, 3);

    // Any checks happen only here
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RevisionedDocuments.cs#L27-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_numeric_revisioning' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
Not sure why you'd do this on purpose, but you can happily supply a version to `UpdateVersion()` or `TryUpdateVersion()`
that is not the current version + 1 as long as that supplied version is greater than the current version, Marten will persist
the document with that new version. This was done purposely to support projected aggregations in the event sourcing functionality.
:::
