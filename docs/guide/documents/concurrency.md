# Optimistic Concurrency

Recent versions of Marten (&gt;0.9.5) have a new feature that allows you to enforce offline optimistic concurrency checks against documents that you are attempting to persist. You would use this feature if you're concerned about a document in your current session having been modified by another session since you originally loaded the document.

I first learned about this concept from Martin Fowler's [PEAA book](http://martinfowler.com/eaaCatalog/). From [Fowler's definition](http://martinfowler.com/eaaCatalog/optimisticOfflineLock.html), offline optimistic concurrency:

> Prevents conflicts between concurrent business transactions by detecting a conflict and rolling back the transaction.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/optimistic_concurrency.cs#L641-L651' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_useoptimisticconcurrencyattribute' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/optimistic_concurrency.cs#L21-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-optimistic-concurrency' title='Start of snippet'>anchor</a></sup>
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
    using (var session = theStore.OpenSession())
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

        ex.Message.ShouldBe($"Optimistic concurrency check failed for {typeof(CoffeeShop).FullName} #{doc1.Id}");
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/optimistic_concurrency.cs#L130-L174' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_update_with_stale_version_standard' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten is throwing an AggregateException for the entire batch of changes being persisted from SaveChanges()/SaveChangesAsync() after rolling back the current database transaction. The individual ConcurrencyException's inside of the aggregated exception expose information about the actual document type and identity that failed.

## Overriding Optimistic Concurrency in Session

Marten allows overriding store-wide optimistic concurrency settings within a session via `SessionOptions`, whereby the `ConcurrencyChecks` can be set to `ConcurrencyChecks.Disabled`.

<!-- snippet: sample_sample-override-optimistic-concurrency -->
<a id='snippet-sample_sample-override-optimistic-concurrency'></a>
```cs
var session1 = theStore.OpenSession(new SessionOptions
{
    ConcurrencyChecks = ConcurrencyChecks.Disabled,
    Tracking = DocumentTracking.DirtyTracking
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/optimistic_concurrency.cs#L185-L191' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-override-optimistic-concurrency' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## How it works

The optimistic concurrency checks work by adding a new argument to the _upsert_ function for a document type to represent the
current version that was loaded by the current session. If that argument matches the value in the database column, everything works
as normal. If the current version does not match the existing version in the database, the upsert function returns some data
that tells the Marten client that there was a version mismatch. After Marten submits all the document changes in a call to
`SaveChanges()/SaveChangesAsync()`, it checks for concurrency violations across all documents and throws a single `AggregatedException`
with all the detected violations.

## Storing a Document with a Given Version

To designate the version of the document you're trying to store, you can use the `IDocumentSession.Store(doc, version)` method
shown below:

<!-- snippet: sample_store_with_the_right_version -->
<a id='snippet-sample_store_with_the_right_version'></a>
```cs
[Fact]
public void store_with_the_right_version()
{
    var doc1 = new CoffeeShop();
    using (var session = theStore.OpenSession())
    {
        session.Store(doc1);
        session.SaveChanges();
    }

    DocumentMetadata metadata;
    using (var session = theStore.QuerySession())
    {
        metadata = session.MetadataFor(doc1);
    }

    using (var session = theStore.OpenSession())
    {
        doc1.Name = "Mozart's";
        session.Store(doc1, metadata.CurrentVersion);

        session.SaveChanges();
    }

    using (var query = theStore.QuerySession())
    {
        query.Load<CoffeeShop>(doc1.Id).Name
            .ShouldBe("Mozart's");
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/optimistic_concurrency.cs#L453-L485' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_store_with_the_right_version' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This method might come in handy if you detect that a document in your current session has been changed by another session.
