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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Concurrency/optimistic_concurrency.cs#L839-L849' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_useoptimisticconcurrencyattribute' title='Start of snippet'>anchor</a></sup>
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

Marten is throwing an AggregateException for the entire batch of chang

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

Your document type will have the optimistic concurrency checks applied to updates *when* the current version is given to Marten. Moreover, the current version
will always be written to the `IVersioned.Version` property when the document is modified or loaded by Marten. This makes `IVersioned` an easy strategy to track
the current version of documents in web applications.
