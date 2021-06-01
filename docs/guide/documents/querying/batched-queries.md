# Batched Queries

For the sake of performance, if you have a case where you may need to fetch several sets of document data from Marten
at one time, you can opt to batch those queries into a single request to the underlying database to reduce network round trips.

The mechanism for doing this is the `IBatchedQuery` object that you can create with the `IQuerySession.CreateBatchQuery()` method.
The batched queries in Marten work by allowing a user to define the queries they want to run through the batch and getting back
a .Net `Task` that can be used to retrieve the actual results later after the batch is finished. When the batch is executed,
Marten combines all the queries into a single command sent to the underlying Postgresql database, then reads through all
the data returned and sets the results of the `Task` objects handed out earlier.

This functionality is demonstrated below:

<!-- snippet: sample_using-batch-query -->
<!-- endSnippet -->

## Combining Compiled Queries and Batch Queries

As of v0.8.10, Marten allows you to incorporate <[linkto:documentation/documents/querying/compiled_queries;title=compiled queries]> as part of a batch query. The Marten team is hoping that this combination will make it easier to create performant applications where you may need to aggregate many documents in a single HTTP request or other operation.

Say you have a compiled query that finds the first user with a given first name:

<!-- snippet: sample_FindByFirstName -->
<!-- endSnippet -->

To use that compiled query class in a batch query, you simply use the `IBatchedQuery.Query(ICompiledQuery)` syntax shown below:

<!-- snippet: sample_batch-query-with-compiled-queries -->
<!-- endSnippet -->

## Running Synchronously

As of v0.9.1, Marten also exposes the batch querying functionality with a synchronous option:

<!-- snippet: sample_batch-query-with-compiled-queries-synchronously -->
<!-- endSnippet -->

The mechanics of running synchronously are identical except for calling `IBatchedQuery.ExecuteSynchronously()`.
