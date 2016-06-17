<!--Title:Optimistic Concurrency-->

Recent versions of Marten (&gt;0.9.5) have a new feature that allows you to enforce offline optimistic concurrency checks against documents that you are attempting to persist. You would use this feature if you're concerned about a document in your current session having been modified by another session since you originally loaded the document.

I first learned about this concept from Martin Fowler's [PEAA book](http://martinfowler.com/eaaCatalog/). From [Fowler's definition](http://martinfowler.com/eaaCatalog/optimisticOfflineLock.html), offline optimistic concurrency:

<blockquote><i>Prevents conflicts between concurrent business transactions by detecting a conflict and rolling back the transaction.</i></blockquote>

In Marten's case, you have to explicitly opt into optimistic versioning for each document type. You can do that with either an attribute on your document type like so:

<[sample:UseOptimisticConcurrencyAttribute]>

Or by using Marten's configuration API to do it programmatically:

<[sample:configuring-optimistic-concurrency]>

Once optimistic concurrency is turned on for the CoffeeShop document type, a session will now only be able to update a document if the document has been unchanged in the database since it was initially loaded.

To demonstrate the failure case, consider the following  acceptance test from Marten's codebase:

<[sample:update_with_stale_version_standard]>

Marten is throwing an AggregateException for the entire batch of changes being persisted from SaveChanges()/SaveChangesAsync() after rolling back the current database transaction. The individual ConcurrencyException's inside of the aggregated exception expose information about the actual document type and identity that failed.


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

<[sample:store_with_the_right_version]>

This method might come in handy if you detect that a document in your current session has been changed by another session.